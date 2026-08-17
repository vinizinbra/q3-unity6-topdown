using System.Collections.Generic;
using Photon.Deterministic;
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
public class DirectorTimelineUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private TMP_Text timerText;
    [SerializeField, Tooltip("Container for the bar's visible children (timelineSlider/timerText/markerRoot) - toggled off during GameState.Boss so this doesn't visually compete with BossWidget. Must be a CHILD GameObject, not the GameObject this script itself lives on, since QUpdate stops firing once its own GameObject is disabled.")]
    private GameObject visualRoot;

    [Header("Phase markers")]
    [SerializeField, Tooltip("Should span the same width as the slider's fill area - markers are placed along it by anchor fraction (0..1), so its own pivot/anchor setup doesn't matter.")]
    private RectTransform markerRoot;
    [SerializeField, Tooltip("Instantiated once per finite phase boundary (Phases.Length - 1 of them) as a child of markerRoot. Its own Icon reference is shown only for a boundary whose upcoming phase has a configured icon, hidden for a plain Combat transition.")]
    private DirectorPhaseMarkerWidget markerPrefab;

    private readonly List<DirectorPhaseMarkerWidget> _spawnedMarkers = new();
    private FP _timelineDuration;
    private bool _markersBuilt;

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        bool isBoss = frame.Global->CurrentState == GameState.Boss;
        SetShown(visualRoot, isBoss == false);

        if (isBoss)
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
                SpawnMarker((cumulative / _timelineDuration).AsFloat, SpriteManager.GetSprite(nextKind.ToString()));
        }

        // markerPrefab is only a template - it may be sitting active in the scene for editing
        // convenience, so force it off once its clones exist rather than relying on it having
        // been left disabled by hand.
        markerPrefab.gameObject.SetActive(false);
    }

    private void SpawnMarker(float normalizedTime, Sprite icon)
    {
        if (markerRoot == null || markerPrefab == null)
            return;

        DirectorPhaseMarkerWidget marker = Instantiate(markerPrefab, markerRoot);
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

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null || go.activeSelf == shown)
            return;

        go.SetActive(shown);
    }
}
