using System.Collections.Generic;
using NaughtyAttributes;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "Generating level..." screen, and the last thing shown before a match actually starts.
//
// Lives in the MENU, as a real UiWindow under MainMenuTab's own WindowManager, rather than in
// QuantumGameScene: by the time the gameplay scene exists, most of the wait is already over
// (SessionRunner.StartAsync is what loads it), so a screen living there can only ever cover the tail
// of the load - and it has to fight the gameplay HUD's own Canvas to do it. The menu Canvas, by
// contrast, is already up and already covering the screen from the moment Play is clicked, so the
// whole chain (MainMenuWindow -> ConnectingWindow -> LoadingWindow -> InMatchWindow) is one
// continuous, uninterrupted overlay with no window where a half-built level is visible.
//
// The hand-off is the point: MatchMakingConfig.StartRunner used to show InMatchWindow the moment
// AddPlayer returned, which disables the menu Canvas (see InMatchWindow.Show) and therefore reveals
// a level that hasn't been generated yet and a hero that hasn't spawned yet. It shows THIS window
// instead, and this window shows InMatchWindow itself once the local hero is genuinely standing in
// the world - so the menu Canvas goes down exactly once there's something worth looking at.
//
// Progress is a weighted sum of pieces, weighted by what actually COSTS time, not by what's easy to
// count. The simulation's own generation cursor (Global.LevelGenCursor/LevelGenTotal) is real but
// nearly free - a few ticks - so it only owns a sliver. The expensive part is the View building the
// world those ticks describe: instantiating every chunk prefab (tiles, colliders, detail scatter),
// then the WaterShoreBaker probe pass (the single biggest cost when measured - time-sliced for that reason), so "chunk entities that have a live view" is what
// gets the bulk of the bar. Only the session/scene load has nothing to count and crawls. The bar is
// monotonic throughout, since a loading bar that goes backwards reads as a bug even when the
// underlying numbers are honest.
public class LoadingWindow : UiWindow
{
    private enum LoadingStage
    {
        Connecting,
        GeneratingLevel,
        BuildingWorld,
        Entering,
    }

    [SerializeField, Tooltip("Faded to 0 right before handing off to InMatchWindow, which is what reveals the world underneath. Optional - without it the hand-off is a hard cut.")]
    private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("Objects OUTSIDE this window's own hierarchy that must fade out with it - typically the menu background sitting behind it, which the fade would otherwise reveal instead of the game. A CanvasGroup is added automatically to anything here that doesn't have one, so a plain background object can be dropped in as-is.")]
    private GameObject[] fadeWithScreen;

    [Header("Readout")]
    [SerializeField, Tooltip("Stage label - CONNECTING / GENERATING LEVEL / BUILDING WORLD / ENTERING THE RIFT. Left unassigned to skip.")]
    private TMP_Text statusText;
    [SerializeField, Tooltip("Percentage readout, e.g. \"42%\". Left unassigned to skip.")]
    private TMP_Text percentText;
    [SerializeField, Tooltip("Progress bar, driven 0..1. Left unassigned to skip.")]
    private Slider progressSlider;
    [SerializeField, Tooltip("Optional rotating hint line. Hidden entirely when tips is empty.")]
    private TMP_Text tipText;
    [SerializeField, Tooltip("Hints cycled while the screen is up, one every tipInterval seconds. Empty hides the tip line.")]
    private string[] tips;
    [SerializeField] private float tipInterval = 4f;

    [Header("Labels")]
    [SerializeField] private string connectingLabel = "CONNECTING";
    [SerializeField] private string generatingLabel = "GENERATING LEVEL";
    [SerializeField] private string buildingLabel = "BUILDING WORLD";
    [SerializeField] private string enteringLabel = "ENTERING THE RIFT";

    [Header("Timing")]
    [SerializeField, Tooltip("The screen never hands off sooner than this, so a fast local start doesn't flash it for two frames.")]
    private float minimumDisplayDuration = 1f;
    [SerializeField, Tooltip("Failsafe: hand off to InMatchWindow anyway after this long even if the hero never showed up, so a bad join can never trap the player behind a screen they can't dismiss. 0 disables the failsafe.")]
    private float maximumDisplayDuration = 45f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private Ease fadeOutEase = Ease.InQuad;
    [SerializeField, Tooltip("How fast the bar eases toward its true value, in bar units per second - purely cosmetic smoothing on top of the real progress. Keep it high enough (~3) that real progress reads as progress rather than as a fixed-length animation.")]
    private float barFillSpeed = 3f;
    [SerializeField, Tooltip("Rate (per second) the countless connecting/scene-load piece closes the gap toward ~95% of itself. Asymptotic, so a slow scene load keeps creeping instead of stalling at a hard cap.")]
    private float connectingCrawlRate = 0.6f;
    [SerializeField, Tooltip("Any single frame longer than this (seconds) while the screen is up is logged with the current stage and counts - the main-thread spikes the bar can't animate through. 0 disables.")]
    private float spikeLogThreshold = 0.05f;

    // Share of the bar each piece owns, by measured cost (Editor Profiler, see docs/loading-screen.md) -
    // the shore bake outweighed instantiating every chunk view. Sums to 1.
    private const float ConnectingWeight = 0.10f;
    private const float GeneratingWeight = 0.10f;
    private const float BuildingWeight = 0.35f;
    private const float BakeWeight = 0.30f;
    private const float EnteringWeight = 0.15f;
    private const float ConnectingCrawlCap = 0.95f;

    private static QuantumEntityViewUpdater _viewUpdater;

    private bool _handingOff;
    private float _elapsed;
    private float _crawl;
    private int _chunkViewsBuilt;
    private int _chunkViewsExpected;
    private float _target;
    private float _displayed;
    private float _tipTimer;
    private int _tipIndex;
    private LoadingStage _stage;
    private Tween _fadeTween;
    private bool _suppressBackgroundRestore;
    private bool _faded;
    private List<CanvasGroup> _fadeGroups;

    // Android can suspend the whole process for an arbitrary length of time. PrimeTween's
    // useUnscaledTime tweens are NOT clamped by Time.maximumDeltaTime (that only clamps the scaled
    // Time.deltaTime), so a hand-off fade left running when the app backgrounds would otherwise wake
    // up to a single huge unscaled delta and jump through its curve unpredictably instead of landing
    // cleanly - reading as a flash right as the menu Canvas goes down and the world is revealed.
    // Snap straight to the fade's own end state and finish the hand-off the same way OnComplete
    // would have, instead of leaving that to chance.
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus == true || _fadeTween.isAlive == false)
            return;

        _fadeTween.Stop();
        ApplyFadeAlpha(0f);
        EnterMatch();
    }

    public override void Show()
    {
        // Only a genuine (re)open restarts the screen - a redundant ShowWindow<LoadingWindow>() for
        // a session start already in progress must not rewind the bar to 0 or restart the failsafe.
        bool wasHidden = gameObject.activeSelf == false;

        base.Show();
        WaterShoreBaker.IsLoadingScreenUp = true;

        if (wasHidden == true)
            ResetToStart();
    }

    public override void Hide()
    {
        // Another window pre-empting this one (the StartRunner failure path shows MainMenuWindow, for
        // instance) must not leave a fade tween running that would then hand off to InMatchWindow
        // from underneath it - _handingOff stays true so Update can't restart either.
        _fadeTween.Stop();

        // Anything faded along with this screen is put back to full alpha on every hide EXCEPT the
        // one this window's own hand-off causes (see EnterMatch): there, the whole point is that the
        // background stays transparent so the world shows through, and it can't be restored by
        // assuming it's about to be hidden with the menu Canvas - it may well be a separate object
        // that isn't. Every other hide - pre-empted mid-load, or simply hidden again later when the
        // player returns to the menu - restores it, so the menu is never left invisible.
        // _faded gates it: WindowManager.ShowWindow calls Hide on EVERY window that isn't the one
        // being shown, so without it every ordinary menu navigation would write alpha 1 over a
        // background this screen never touched.
        if (_faded == true && _suppressBackgroundRestore == false)
        {
            ApplyFadeAlpha(1f);
            _faded = false;
        }

        WaterShoreBaker.IsLoadingScreenUp = false;
        base.Hide();
    }

    private void Update()
    {
        if (_handingOff == true)
            return;

        // Unscaled throughout - a loading screen has to keep animating regardless of what the
        // simulation or any client-local time ramp is doing.
        float deltaTime = Time.unscaledDeltaTime;
        _elapsed += deltaTime;

        Frame frame = ResolveFrame();
        LoadingStage stage = ResolveStage(frame, deltaTime);

        ApplyStatus(stage);
        ApplyProgress(deltaTime);
        ApplyTip(deltaTime);

        // Main-thread spikes are exactly what a bar can't animate through (it freezes, then jumps),
        // so name them - this is how to see which piece is actually worth time-slicing.
        if (spikeLogThreshold > 0f && deltaTime > spikeLogThreshold)
            LogHelper.Warn("Loading", $"{deltaTime * 1000f:0}ms frame in stage {stage} (chunk views {_chunkViewsBuilt}/{_chunkViewsExpected}, bar {_target:0.00}, {_elapsed:0.0}s in)", this);

        if (_elapsed < minimumDisplayDuration)
            return;

        if (IsWorldReady(frame) == true)
        {
            HandOff();
            return;
        }

        if (maximumDisplayDuration > 0f && _elapsed >= maximumDisplayDuration)
        {
            LogHelper.Warn("Loading", $"timed out after {maximumDisplayDuration:0.#}s in stage {stage} without a local hero - entering the match anyway.", this);
            HandOff();
        }
    }

    // The one thing this screen is actually waiting for: a local hero that exists AND has registered
    // its view (MyLocalPlayer.Register runs off CharView, so this is true only once there's genuinely
    // something on screen to look at, not merely once the entity was created in the simulation).
    //
    // Falls back to the simulation's own readiness gate for a client with no local player at all -
    // without it a spectator would sit behind this screen until the failsafe timeout.
    //
    // Also holds for the shore-field bake: it's time-sliced (WaterShoreBaker.probeBudgetMs), so on a
    // slow device it can still be running when the hero appears - lifting the screen then would
    // reveal water with no shoreline foam, which then pops in. No baker in the scene = not pending.
    // The maximumDisplayDuration failsafe still applies, so a stuck bake can't trap the player.
    private bool IsWorldReady(Frame frame)
    {
        if (WaterShoreBaker.IsBakePending == true)
            return false;

        if (MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.AnyLocalPlayerSetup == true)
            return true;

        return frame != null && PlayerSpawnUtility.IsReadyToSpawn(frame);
    }

    private static Frame ResolveFrame()
    {
        QuantumRunner runner = QuantumRunner.Default;

        if (runner == null || runner.Game == null || runner.Game.Frames == null)
            return null;

        // Null until the session has actually simulated its first tick - which is most of what the
        // Connecting stage is waiting on.
        return runner.Game.Frames.Predicted;
    }

    // Fills _target from the weighted pieces (see the class comment) and returns the stage named by
    // the first piece that isn't done yet.
    private unsafe LoadingStage ResolveStage(Frame frame, float deltaTime)
    {
        if (frame == null)
        {
            // Session start + gameplay scene load: nothing to count, so close the gap asymptotically
            // - a slow load keeps visibly creeping rather than parking at a hard cap.
            _crawl += (ConnectingCrawlCap - _crawl) * (1f - Mathf.Exp(-connectingCrawlRate * deltaTime));
            _target = ConnectingWeight * _crawl;
            return LoadingStage.Connecting;
        }

        bool levelGenerated = frame.Global->LevelGenerated;
        int genTotal = frame.Global->LevelGenTotal;

        // Total is only published on the first generation tick, so until then it's just 0.
        float generating = levelGenerated ? 1f
            : genTotal > 0 ? Mathf.Clamp01(frame.Global->LevelGenCursor / (float)genTotal) : 0f;
        float building = ResolveChunkViewProgress(frame, levelGenerated, genTotal);
        // Time-sliced over frames (see WaterShoreBaker.probeBudgetMs), so this is real, visible progress.
        float baked = levelGenerated ? WaterShoreBaker.BakeProgress : 0f;
        float entering = ResolveEnteringProgress(frame, levelGenerated);

        _target = ConnectingWeight
                  + GeneratingWeight * generating
                  + BuildingWeight * building
                  + BakeWeight * baked
                  + EnteringWeight * entering;

        if (levelGenerated == false)
            return LoadingStage.GeneratingLevel;

        if (building < 1f || baked < 1f)
            return LoadingStage.BuildingWorld;

        return LoadingStage.Entering;
    }

    // Chunk entities whose prefab view actually exists, over how many there will be. This is the
    // real cost of a match start - the sim places a chunk in a fraction of a tick, the View then has
    // to instantiate its whole prefab. Only entities carrying a View component count (a chunk with no
    // view asset would otherwise never complete). Until generation finishes the denominator is at
    // least LevelGenTotal, so views keeping pace with a half-generated level can't read as done;
    // FillInnerGaps adds chunks past that total on the last tick, which the live count picks up.
    private unsafe float ResolveChunkViewProgress(Frame frame, bool levelGenerated, int genTotal)
    {
        if (_viewUpdater == null)
            _viewUpdater = FindFirstObjectByType<QuantumEntityViewUpdater>();

        int withView = 0;
        int built = 0;

        var chunks = frame.Filter<Chunk>();
        while (chunks.Next(out EntityRef entity, out Chunk _))
        {
            if (frame.Has<View>(entity) == false)
                continue;

            withView++;

            if (_viewUpdater != null && _viewUpdater.GetView(entity) != null)
                built++;
        }

        _chunkViewsBuilt = built;
        _chunkViewsExpected = levelGenerated ? withView : Mathf.Max(withView, genTotal);

        if (_chunkViewsExpected == 0)
            return levelGenerated ? 1f : 0f;

        return Mathf.Clamp01(built / (float)_chunkViewsExpected);
    }

    // The spawn settle delay (real sim time, PlayerSpawnUtility.SpawnDelaySeconds) fills the first
    // part; the local hero's view registering - the hand-off condition itself - fills the rest.
    private unsafe float ResolveEnteringProgress(Frame frame, bool levelGenerated)
    {
        if (levelGenerated == false)
            return 0f;

        if (MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.AnyLocalPlayerSetup == true)
            return 1f;

        float settle = frame.Global->TimeSinceLevelGenerated.AsFloat / PlayerSpawnUtility.SpawnDelaySeconds.AsFloat;
        return 0.7f * Mathf.Clamp01(settle);
    }

    private void ApplyStatus(LoadingStage stage)
    {
        if (stage != _stage)
        {
            // One line per stage, so a genuine hang is diagnosable from the log rather than from
            // squinting at a bar that stopped moving.
            _stage = stage;
            LogHelper.Log("Loading", $"stage -> {stage} ({_elapsed:0.0}s in)", this);
        }

        if (statusText == null)
            return;

        string label = stage switch
        {
            LoadingStage.Connecting => connectingLabel,
            LoadingStage.GeneratingLevel => generatingLabel,
            LoadingStage.BuildingWorld => buildingLabel,
            _ => enteringLabel,
        };

        if (statusText.text != label)
            statusText.text = label;
    }

    private void ApplyProgress(float deltaTime)
    {
        // Monotonic by construction - the bar only ever eases upward, never back down.
        _displayed = Mathf.MoveTowards(_displayed, Mathf.Max(_displayed, _target), barFillSpeed * deltaTime);

        if (progressSlider != null)
            progressSlider.value = _displayed;

        if (percentText != null)
            percentText.text = Mathf.RoundToInt(_displayed * 100f) + "%";
    }

    private void ApplyTip(float deltaTime)
    {
        if (tipText == null)
            return;

        if (tips == null || tips.Length == 0)
        {
            if (tipText.gameObject.activeSelf == true)
                tipText.gameObject.SetActive(false);

            return;
        }

        _tipTimer -= deltaTime;

        if (_tipTimer > 0f)
            return;

        _tipTimer = tipInterval;
        tipText.text = tips[_tipIndex % tips.Length];
        _tipIndex++;
    }

    // Fade this screen out first, THEN show InMatchWindow - in that order, because InMatchWindow.Show
    // disables the whole menu Canvas, which would cut this window off mid-fade. Fading first means the
    // world is revealed underneath while the menu is still up, and the Canvas goes down on a screen
    // that's already fully transparent.
    private void HandOff()
    {
        _handingOff = true;
        _target = 1f;
        _displayed = 1f;

        if (progressSlider != null)
            progressSlider.value = 1f;

        if (percentText != null)
            percentText.text = "100%";

        LogHelper.Log("Loading", $"world is ready after {_elapsed:0.0}s - entering the match.", this);

        _fadeTween.Stop();

        if (ResolveFadeGroups().Count == 0)
        {
            EnterMatch();
            return;
        }

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = false;

        _faded = true;

        _fadeTween = Tween.Custom(1f, 0f, fadeOutDuration,
            onValueChange: v => ApplyFadeAlpha((float)v), ease: fadeOutEase, useUnscaledTime: true)
            .OnComplete(this, target => target.EnterMatch());
    }

    // This window's own CanvasGroup plus every fadeWithScreen entry, so one tween value drives all of
    // them and the screen and its background can never end up at different alphas.
    private List<CanvasGroup> ResolveFadeGroups()
    {
        if (_fadeGroups != null)
            return _fadeGroups;

        _fadeGroups = new List<CanvasGroup>();

        if (canvasGroup != null)
            _fadeGroups.Add(canvasGroup);

        if (fadeWithScreen == null)
            return _fadeGroups;

        foreach (GameObject target in fadeWithScreen)
        {
            if (target == null)
                continue;

            // Added rather than required, so a plain background Image can be dropped into the array
            // without also hand-authoring a CanvasGroup on it. A fresh one defaults to alpha 1, which
            // is exactly the state it should already be in.
            if (target.TryGetComponent(out CanvasGroup group) == false)
                group = target.AddComponent<CanvasGroup>();

            _fadeGroups.Add(group);
        }

        return _fadeGroups;
    }

    private void ApplyFadeAlpha(float alpha)
    {
        foreach (CanvasGroup group in ResolveFadeGroups())
        {
            if (group != null)
                group.alpha = alpha;
        }
    }

    private void EnterMatch()
    {
        WindowManager windowManager = ResolveWindowManager();

        if (windowManager == null)
        {
            LogHelper.Error("Loading", "no WindowManager found - can't hand off to InMatchWindow, hiding this screen instead.", this);
            Hide();
            return;
        }

        // This also hides THIS window (ShowWindow hides everything that isn't the requested type),
        // which is what takes the screen down - there's deliberately no self-Hide here. The flag is
        // what tells that Hide to leave the faded-out background alone; see Hide's own comment.
        _suppressBackgroundRestore = true;
        windowManager.ShowWindow<InMatchWindow>();
        _suppressBackgroundRestore = false;
    }

    // Its own parent manager first, so this works regardless of how the menu is wired up; GameManager
    // is the fallback every other window in the menu goes through.
    private WindowManager ResolveWindowManager()
    {
        WindowManager parent = GetComponentInParent<WindowManager>(true);

        if (parent != null)
            return parent;

        return GameManager.Instance != null && GameManager.Instance.MainMenuTab != null
            ? GameManager.Instance.MainMenuTab.windowManager
            : null;
    }

    private void ResetToStart()
    {
        _handingOff = false;
        _elapsed = 0f;
        _crawl = 0f;
        _chunkViewsBuilt = 0;
        _chunkViewsExpected = 0;
        _target = 0f;
        _displayed = 0f;
        _tipTimer = 0f;
        _tipIndex = 0;
        _stage = LoadingStage.Connecting;

        _suppressBackgroundRestore = false;
        _faded = false;

        // Puts the background back too - a second match start has to begin with a fully visible
        // screen even though the previous hand-off deliberately left it transparent.
        ApplyFadeAlpha(1f);

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = 0f;
        }
    }

    // Play Mode iteration - re-runs the whole screen against the live match so the layout/animation
    // can be tuned without restarting. Its own hand-off condition is already true mid-match, so it
    // plays out the minimum display duration and then fades, which is the part worth looking at.
    [Button("Replay Loading Screen (Debug)")]
    private void ReplayForDebug()
    {
        _fadeTween.Stop();
        base.Show();
        ResetToStart();
    }
}
