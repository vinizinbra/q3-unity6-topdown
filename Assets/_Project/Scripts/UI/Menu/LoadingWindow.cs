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
// Progress is real, not faked, for the stage that actually takes time: LevelGenerationSystem spreads
// generation over many ticks and publishes its own cursor/total on Global (see Chunk.qtn's own
// comment naming this screen as their consumer). The two stages either side of it have no countable
// work, so they crawl toward their band's end instead - the bar is monotonic throughout, since a
// loading bar that goes backwards reads as a bug even when the underlying numbers are honest.
public class LoadingWindow : UiWindow
{
    private enum LoadingStage
    {
        Connecting,
        GeneratingLevel,
        Entering,
    }

    [SerializeField, Tooltip("Faded to 0 right before handing off to InMatchWindow, which is what reveals the world underneath. Optional - without it the hand-off is a hard cut.")]
    private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("Objects OUTSIDE this window's own hierarchy that must fade out with it - typically the menu background sitting behind it, which the fade would otherwise reveal instead of the game. A CanvasGroup is added automatically to anything here that doesn't have one, so a plain background object can be dropped in as-is.")]
    private GameObject[] fadeWithScreen;

    [Header("Readout")]
    [SerializeField, Tooltip("Stage label - CONNECTING / GENERATING LEVEL / ENTERING THE RIFT. Left unassigned to skip.")]
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
    [SerializeField] private string enteringLabel = "ENTERING THE RIFT";

    [Header("Timing")]
    [SerializeField, Tooltip("The screen never hands off sooner than this, so a fast local start doesn't flash it for two frames.")]
    private float minimumDisplayDuration = 1f;
    [SerializeField, Tooltip("Failsafe: hand off to InMatchWindow anyway after this long even if the hero never showed up, so a bad join can never trap the player behind a screen they can't dismiss. 0 disables the failsafe.")]
    private float maximumDisplayDuration = 45f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private Ease fadeOutEase = Ease.InQuad;
    [SerializeField, Tooltip("How fast the bar eases toward its true value, in bar units per second - purely cosmetic smoothing on top of the real progress.")]
    private float barFillSpeed = 1.5f;
    [SerializeField, Tooltip("How fast the two countless stages (connecting, entering) creep toward the end of their own band, in bar units per second.")]
    private float crawlSpeed = 0.15f;

    // Band each stage owns on the bar. Generation is the only stage with countable work, so it gets
    // the bulk of the bar; the other two exist so the bar is already moving before/after it.
    private const float ConnectingBandEnd = 0.15f;
    private const float GeneratingBandEnd = 0.85f;

    private bool _handingOff;
    private float _elapsed;
    private float _crawl;
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
    private bool IsWorldReady(Frame frame)
    {
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

    private unsafe LoadingStage ResolveStage(Frame frame, float deltaTime)
    {
        if (frame == null)
        {
            _target = Crawl(0f, ConnectingBandEnd, deltaTime);
            return LoadingStage.Connecting;
        }

        if (frame.Global->LevelGenerated == false)
        {
            int total = frame.Global->LevelGenTotal;
            int cursor = frame.Global->LevelGenCursor;

            // Total is only published once the first generation tick has built its request bag, so
            // until then this stage has nothing to count either and crawls like the others.
            _target = total > 0
                ? Mathf.Lerp(ConnectingBandEnd, GeneratingBandEnd, Mathf.Clamp01(cursor / (float)total))
                : Crawl(ConnectingBandEnd, GeneratingBandEnd, deltaTime);

            return LoadingStage.GeneratingLevel;
        }

        _target = PlayerSpawnUtility.IsReadyToSpawn(frame)
            ? 1f
            : Crawl(GeneratingBandEnd, 1f, deltaTime);

        return LoadingStage.Entering;
    }

    // Advances a stage that has no countable work toward the end of its own band. Kept in its own
    // accumulator (rather than reusing the displayed value) so entering a stage never rewinds the bar.
    private float Crawl(float bandStart, float bandEnd, float deltaTime)
    {
        _crawl = Mathf.Max(_crawl, bandStart);
        _crawl = Mathf.MoveTowards(_crawl, bandEnd, crawlSpeed * deltaTime);
        return _crawl;
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
