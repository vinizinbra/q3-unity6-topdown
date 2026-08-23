using System.Collections.Generic;
using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD readout for the Survival Director's match clock - fill slider + MM:SS text, with phase-
// transition markers spawned once along the slider from SurvivalConfig.Phases. Total timeline is
// the sum of every COMBAT phase's Duration (Breathing phases excluded entirely - see
// BuildMarkersOnce's own comment), including the last combat phase - that Duration is never
// checked by SurvivalProgressionUtility to trigger a transition (the run just holds at the last
// phase forever), but it still counts here as "when the ramp is considered complete" for display.
// Once SurvivalTime passes that sum the slider just clamps full while the text keeps counting up.
//
// The bar hides itself once a Breathing Break's area is actually SECURED (not the instant the Break
// begins - the leftover enemies are still combat, and BreathingCountdownWidget only takes the screen
// once they're cleared) and comes back only once the SURVIVAL MODE STARTED reveal that announces the
// next combat phase has fully played out - see SurvivalStartedWidget.RevealCompleted. Both
// transitions are a scale down / scale up of the authored scaleTargets (the bar background and
// markerRoot - deliberately not the MM:SS timer text, which stays put) rather than a bare SetActive
// snap. Losing the banner slot outright to Boss/TraversalChallenge is the one case that still takes
// the whole of visualRoot off, timer text included.
public class DirectorTimelineUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private TMP_Text timerText;
    [SerializeField, Tooltip("Container for the bar's visible children (timelineSlider/timerText/markerRoot) - scaled down and toggled off whenever Global.HudBanner isn't DirectorTimeline (Boss/TraversalChallenge currently own the banner slot instead - see GameState.qtn's own HudBannerKind comment) or a Breathing Break's area has been secured, so this doesn't visually compete with BossWidget/TraversalChallengeWidget/BreathingCountdownWidget. Its authored localScale is what the bar scales back up to. Must be a CHILD GameObject, not the GameObject this script itself lives on, since QUpdate stops firing once its own GameObject is disabled.")]
    private GameObject visualRoot;

    [Header("Phase markers")]
    [SerializeField, Tooltip("Should span the same width as the slider's fill area - markers are placed along it by anchor fraction (0..1), so its own pivot/anchor setup doesn't matter.")]
    private RectTransform markerRoot;
    [SerializeField, Tooltip("Instantiated once per finite phase boundary (Phases.Length - 1 of them) as a child of markerRoot. Its own Icon reference is shown only for a boundary whose upcoming phase has a configured icon, hidden for a plain Combat transition.")]
    private DirectorPhaseMarkerWidget markerPrefab;

    [Header("Show/hide animation")]
    [SerializeField, Tooltip("The parts that actually scale away - typically just the bar background and markerRoot, so anything left out (the MM:SS timer text) stays on screen the whole time. Left empty, the whole of visualRoot scales instead. Each entry keeps its own authored localScale as the size it returns to, and visualRoot itself is still what gets deactivated once everything listed has scaled down.")]
    private Transform[] scaleTargets;
    [SerializeField, Tooltip("Scale-up duration when the bar comes back (after the SURVIVAL MODE STARTED reveal finishes).")]
    private float scaleInDuration = 0.3f;
    [SerializeField] private Ease scaleInEase = Ease.OutBack;
    [SerializeField, Tooltip("Scale-down duration when the bar leaves (a Breathing Break starting, or another banner taking the slot).")]
    private float scaleOutDuration = 0.2f;
    [SerializeField] private Ease scaleOutEase = Ease.InBack;

    private readonly List<DirectorPhaseMarkerWidget> _spawnedMarkers = new();
    private FP _timelineDuration;
    private bool _markersBuilt;

    private Transform[] _resolvedScaleTargets;
    private Vector3[] _restScales;
    private bool _canDeactivateVisualRoot;
    private Sequence _scaleSequence;
    private bool _shown;
    private bool _deactivateWhenHidden;
    private bool _visibilityInitialized;
    private bool _wasBreathing;
    // Latched on the Breathing -> Survival edge and cleared by SurvivalStartedWidget.RevealCompleted,
    // so the bar waits out that banner instead of popping back in underneath it. Deliberately a latch
    // rather than polling an "is the reveal playing" flag: the two widgets' QUpdates run in an
    // unspecified order, so on the transition tick itself the reveal may not have started yet.
    private bool _awaitingSurvivalReveal;

    private void Awake()
    {
        ResolveScaleTargets();
    }

    // Captured before anything ever scales them, so every later scale-up returns to the authored
    // size rather than compounding whatever the last tween left behind.
    private void ResolveScaleTargets()
    {
        bool hasExplicitTargets = scaleTargets != null && scaleTargets.Length > 0;

        _resolvedScaleTargets =
            hasExplicitTargets == true ? scaleTargets
            // Default when nothing is authored: the phase markers leave, the MM:SS timer stays.
            // This used to fall back to visualRoot - the whole bar, timer included - so an
            // unauthored scaleTargets silently took the clock with it.
            : markerRoot != null ? new[] { (Transform)markerRoot }
            // Only reachable with markerRoot unassigned too, i.e. a half-authored widget. Keeps the
            // old whole-bar behaviour rather than animating nothing at all.
            : visualRoot != null ? new[] { visualRoot.transform }
            : System.Array.Empty<Transform>();

        // Whether deactivating visualRoot is ever an option at all. With explicit targets it isn't
        // for a mere secured Break - that would take everything NOT listed (the timer text) off
        // screen too, the exact opposite of the point, and a zero-scaled target already renders
        // nothing. Losing the banner slot entirely still deactivates either way (see QUpdate).
        _canDeactivateVisualRoot = hasExplicitTargets == false && markerRoot == null;

        _restScales = new Vector3[_resolvedScaleTargets.Length];

        for (int i = 0; i < _resolvedScaleTargets.Length; i++)
        {
            if (_resolvedScaleTargets[i] != null)
                _restScales[i] = _resolvedScaleTargets[i].localScale;
        }
    }

    private void OnEnable()
    {
        SurvivalStartedWidget.RevealCompleted += OnSurvivalRevealCompleted;
    }

    private void OnDisable()
    {
        SurvivalStartedWidget.RevealCompleted -= OnSurvivalRevealCompleted;
    }

    private void OnSurvivalRevealCompleted()
    {
        _awaitingSurvivalReveal = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        // Entering a Break isn't enough on its own to drop the bar - the enemies still on the field
        // are combat, and PhaseTimer (so the Break's own countdown) doesn't even start until they're
        // gone. It leaves only once the area is actually secured, which is the same moment
        // BreathingCountdownWidget takes the screen with AREA SECURED / NEXT ASSAULT.
        bool isBreathing = frame.Global->CurrentState == GameState.Breathing;
        bool isSecuredBreak = isBreathing == true && frame.Global->BreathingAreaSecured == true;

        // Breathing -> Survival: hold the bar back until the SURVIVAL MODE STARTED banner is done.
        // Only worth waiting on when that widget actually exists in the scene - unauthored, nothing
        // would ever clear the latch and the bar would never return.
        if (_wasBreathing == true && isBreathing == false
            && frame.Global->CurrentState == GameState.Survival
            && SurvivalStartedWidget.IsPresent == true)
        {
            _awaitingSurvivalReveal = true;
        }

        _wasBreathing = isBreathing;

        // Safety net for the banner disappearing mid-reveal (its own GameObject disabled, scene
        // teardown, ...) - nothing would be left to raise RevealCompleted, and the bar would stay
        // hidden for the rest of the run.
        if (_awaitingSurvivalReveal == true && SurvivalStartedWidget.IsPresent == false)
            _awaitingSurvivalReveal = false;

        // Losing the banner slot (Boss/Traversal Challenge owning it instead) and a secured Break
        // now behave the same way: only the scaleTargets animate away. The MM:SS timer deliberately
        // survives both - it's the match clock, still worth reading while a Boss or a Traversal
        // Challenge owns the banner slot, and HudBanner's mutual exclusion is about the BAR not
        // competing for that space, not about blanking the clock. Losing the slot used to take the
        // whole of visualRoot off, timer included.
        bool bannerOwned = frame.Global->HudBanner == HudBannerKind.DirectorTimeline;
        bool barShown = bannerOwned == true && isSecuredBreak == false && _awaitingSurvivalReveal == false;

        SetShown(barShown, deactivateRootWhenHidden: false);

        if (bannerOwned == false)
            return;

        if (frame.RuntimeConfig.SurvivalConfig.Id.IsValid == false)
            return;

        SurvivalConfig config = frame.FindAsset(frame.RuntimeConfig.SurvivalConfig);

        if (config.Phases == null || config.Phases.Length == 0)
            return;

        BuildMarkersOnce(config);

        UpdateText(frame.Global->SurvivalTime);

        if (_timelineDuration > FP._0)
            UpdateSlider(frame.Global->SurvivalTime / _timelineDuration);

        UpdateMarkerStates(frame.Global->CurrentPhaseIndex);
    }

    // Before the run reaches a marker's phase it's Before; while the run is IN that phase it's
    // Reached (recolored + idle scale wiggle); once the run advances past it it's Passed. Driven off
    // the current phase index rather than the fill fraction, so it stays exact even though
    // SurvivalTime freezes during a Breathing phase (the marker's own point on the bar).
    private void UpdateMarkerStates(int currentPhaseIndex)
    {
        for (int i = 0; i < _spawnedMarkers.Count; i++)
        {
            DirectorPhaseMarkerWidget marker = _spawnedMarkers[i];

            DirectorPhaseMarkerWidget.MarkerState state =
                currentPhaseIndex < marker.PhaseIndex ? DirectorPhaseMarkerWidget.MarkerState.Before
                : currentPhaseIndex == marker.PhaseIndex ? DirectorPhaseMarkerWidget.MarkerState.Reached
                : DirectorPhaseMarkerWidget.MarkerState.Passed;

            marker.SetState(state);
        }
    }

    private void BuildMarkersOnce(SurvivalConfig config)
    {
        if (_markersBuilt)
            return;

        _markersBuilt = true;

        // Breathing phases are excluded entirely, not just visually de-emphasized - this bar's own
        // numerator (Global.SurvivalTime, see QUpdate) is cumulative COMBAT time only and freezes
        // completely while a phase's Kind is Breathing (see docs/run-phase.md's "Independent
        // timers"). Counting a Breathing phase's own Duration into the total here would make the
        // bar permanently fall short of full even once every combat phase has actually finished,
        // and would misplace every marker placed after the first Breathing entry.
        _timelineDuration = FP._0;
        for (int i = 0; i < config.Phases.Length; i++)
        {
            if (config.Phases[i].Kind != SurvivalPhaseKind.Breathing)
                _timelineDuration += config.Phases[i].Duration;
        }

        if (_timelineDuration <= FP._0)
            return;

        // A marker only exists where a non-Combat phase (Breathing/Elite/Boss) actually begins - a
        // plain combat->combat boundary spawns nothing at all, not just an icon-less marker, so two
        // back-to-back combat phases read as one unbroken stretch on the bar (e.g. two 120s combat
        // phases with nothing between them show as a continuous 240s span, no notch at the seam). A
        // Breathing phase contributes no distance and is skipped outright (both the accumulation and
        // the marker it would otherwise spawn for whatever follows it), so a combat->breathing->combat
        // run of phases still gets exactly one marker, at the real combat-to-combat boundary, not a
        // redundant pair stacked on top of each other at the same normalized position. The marker
        // spawned there represents whichever REAL phase begins right at that point - the very next
        // entry, even when it's a zero-width Breathing phase sitting at this exact position.
        FP cumulative = FP._0;
        for (int i = 0; i < config.Phases.Length - 1; i++)
        {
            if (config.Phases[i].Kind == SurvivalPhaseKind.Breathing)
                continue;

            cumulative += config.Phases[i].Duration;

            SurvivalPhaseKind nextKind = config.Phases[i + 1].Kind;

            // Boss never gets a marker either, unconditionally - it's the run's own terminal phase
            // (the last entry, holds forever - see SurvivalConfig's own comment), so by the time the
            // bar would show it the run is already effectively "done." Same reasoning extends one
            // step further back: a Breathing Break that leads directly into Boss (this run's final
            // beat before the encounter) is skipped too, even though Breathing normally gets a
            // marker - there's nothing left to call out between it and Boss, so marking it would just
            // be visual noise right at the very end of the bar.
            bool isFinalBreathingBeforeBoss = nextKind == SurvivalPhaseKind.Breathing
                && i + 2 < config.Phases.Length
                && config.Phases[i + 2].Kind == SurvivalPhaseKind.Boss;

            // Otherwise every remaining non-Combat kind (Breathing/Elite) is resolved by name
            // (SurvivalPhaseKind.ToString()) through the same shared, name-keyed SpriteManager/
            // SpriteConfigSO lookup CurrencyUiWidget/PurchasableCardUi already use. No dedicated
            // subclass needed - just add entries with these names to any SpriteConfigSO already
            // registered on the scene's SpriteManager (e.g. the existing SpriteConfigCurrency asset).
            // A kind with no matching entry still spawns the marker itself, just with no icon
            // (SpawnMarker reads a null sprite as "hide the icon" below).
            if (nextKind != SurvivalPhaseKind.Combat && nextKind != SurvivalPhaseKind.Boss && isFinalBreathingBeforeBoss == false)
                SpawnMarker((cumulative / _timelineDuration).AsFloat, i + 1, SpriteManager.GetSprite(nextKind.ToString()));
        }

        // markerPrefab is only a template - it may be sitting active in the scene for editing
        // convenience, so force it off once its clones exist rather than relying on it having
        // been left disabled by hand.
        markerPrefab.gameObject.SetActive(false);
    }

    private void SpawnMarker(float normalizedTime, int phaseIndex, Sprite icon)
    {
        if (markerRoot == null || markerPrefab == null)
            return;

        DirectorPhaseMarkerWidget marker = Instantiate(markerPrefab, markerRoot);
        marker.PhaseIndex = phaseIndex;
        RectTransform markerTransform = (RectTransform)marker.transform;
        markerTransform.gameObject.SetActive(true);

        // Anchor-fraction positioning instead of raw anchoredPosition math - places the marker at
        // normalizedTime across markerRoot regardless of markerRoot/marker's own pivot or anchor
        // setup, and keeps it correct if the slider is ever resized.
        markerTransform.anchorMin = new Vector2(normalizedTime, markerTransform.anchorMin.y);
        markerTransform.anchorMax = new Vector2(normalizedTime, markerTransform.anchorMax.y);
        markerTransform.anchoredPosition = new Vector2(0f, markerTransform.anchoredPosition.y);

        Image iconImage = marker.Icon;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }

        _spawnedMarkers.Add(marker);
    }

    private void UpdateSlider(FP normalized)
    {
        if (timelineSlider == null)
            return;

        timelineSlider.value = Mathf.Clamp01(normalized.AsFloat);
    }

    private void UpdateText(FP elapsed)
    {
        if (timerText == null)
            return;

        int totalSeconds = Mathf.FloorToInt(elapsed.AsFloat);
        timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    // Scales every scaleTargets entry down to nothing on hide and back up from nothing on show.
    // deactivateRootWhenHidden additionally takes visualRoot itself off once they've all landed (so
    // nothing is yanked off mid-animation) - true only when the banner slot is lost entirely, since
    // that has to hide the timer text too. useUnscaledTime throughout, matching every other HUD
    // banner here - a Level-Up screen can ramp Time.timeScale down match-wide. Grouped into one
    // Sequence purely so a single handle stops the whole set when the state flips back mid-animation.
    private void SetShown(bool shown, bool deactivateRootWhenHidden)
    {
        if (visualRoot == null || _resolvedScaleTargets == null || _resolvedScaleTargets.Length == 0)
            return;

        bool deactivate = deactivateRootWhenHidden == true || _canDeactivateVisualRoot == true;

        if (_visibilityInitialized == true && _shown == shown && _deactivateWhenHidden == deactivate)
            return;

        bool wasInitialized = _visibilityInitialized;
        bool wasShown = _shown;
        _visibilityInitialized = true;
        _shown = shown;
        _deactivateWhenHidden = deactivate;

        // Already hidden and only the deactivate policy changed (a secured Break that then loses the
        // banner to Boss, say) - the scale-out already played, so just apply the policy.
        if (wasInitialized == true && shown == false && wasShown == false)
        {
            SetVisualRootActive(false);
            return;
        }

        _scaleSequence.Stop();

        // Nothing to animate away from on the very first evaluation - just snap to the resolved state.
        if (wasInitialized == false)
        {
            SnapScales(shown);
            SetVisualRootActive(shown);
            return;
        }

        if (shown == true)
        {
            SetVisualRootActive(true);
            SnapScales(false);
        }

        _scaleSequence = Sequence.Create(useUnscaledTime: true);
        int animated = 0;

        for (int i = 0; i < _resolvedScaleTargets.Length; i++)
        {
            Transform target = _resolvedScaleTargets[i];

            if (target == null)
                continue;

            Tween tween = shown == true
                ? Tween.Scale(target, _restScales[i], scaleInDuration, scaleInEase, useUnscaledTime: true)
                : Tween.Scale(target, Vector3.zero, scaleOutDuration, scaleOutEase, useUnscaledTime: true);

            _scaleSequence.Group(tween);
            animated++;
        }

        if (shown == true || _deactivateWhenHidden == false)
            return;

        // Every entry was null - there's no animation to wait on, so don't hand the deactivate to an
        // empty sequence.
        if (animated == 0)
        {
            SetVisualRootActive(false);
            return;
        }

        GameObject root = visualRoot;
        _scaleSequence.OnComplete(() =>
        {
            if (root != null)
                root.SetActive(false);
        });
    }

    private void SetVisualRootActive(bool active)
    {
        // A hide that isn't allowed to deactivate leaves visualRoot on, carrying whatever isn't in
        // scaleTargets. Activating is unconditional, so a visualRoot left off in the scene still
        // comes on.
        if (active == false && _deactivateWhenHidden == false)
            return;

        if (visualRoot.activeSelf != active)
            visualRoot.SetActive(active);
    }

    private void SnapScales(bool shown)
    {
        for (int i = 0; i < _resolvedScaleTargets.Length; i++)
        {
            if (_resolvedScaleTargets[i] != null)
                _resolvedScaleTargets[i].localScale = shown == true ? _restScales[i] : Vector3.zero;
        }
    }
}
