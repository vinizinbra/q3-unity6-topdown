using System.Collections.Generic;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD readout for the Survival Director's match clock - fill slider + MM:SS text, with phase-
// transition markers spawned once along the slider from SurvivalConfig.Phases. Total timeline is
// the sum of every phase's Duration, including the last - that Duration is never checked by
// SurvivalProgressionUtility to trigger a transition (the run just holds at the last phase
// forever), but it still counts here as "when the ramp is considered complete" for display. Once
// SurvivalTime passes that sum the slider just clamps full while the text keeps counting up.
public class DirectorTimelineUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private TMP_Text timerText;

    [Header("Phase markers")]
    [SerializeField, Tooltip("Should span the same width as the slider's fill area - markers are placed along it by anchor fraction (0..1), so its own pivot/anchor setup doesn't matter.")]
    private RectTransform markerRoot;
    [SerializeField, Tooltip("Instantiated once per finite phase boundary (Phases.Length - 1 of them) as a child of markerRoot.")]
    private RectTransform markerPrefab;

    private readonly List<RectTransform> _spawnedMarkers = new();
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

        _timelineDuration = FP._0;
        for (int i = 0; i < config.Phases.Length; i++)
            _timelineDuration += config.Phases[i].Duration;

        if (_timelineDuration <= FP._0)
            return;

        // One marker per boundary *between* phases - Phases.Length - 1 of them, not one per phase.
        FP cumulative = FP._0;
        for (int i = 0; i < config.Phases.Length - 1; i++)
        {
            cumulative += config.Phases[i].Duration;
            SpawnMarker((cumulative / _timelineDuration).AsFloat);
        }

        // markerPrefab is only a template - it may be sitting active in the scene for editing
        // convenience, so force it off once its clones exist rather than relying on it having
        // been left disabled by hand.
        markerPrefab.gameObject.SetActive(false);
    }

    private void SpawnMarker(float normalizedTime)
    {
        if (markerRoot == null || markerPrefab == null)
            return;

        RectTransform marker = Instantiate(markerPrefab, markerRoot);
        marker.gameObject.SetActive(true);

        // Anchor-fraction positioning instead of raw anchoredPosition math - places the marker at
        // normalizedTime across markerRoot regardless of markerRoot/marker's own pivot or anchor
        // setup, and keeps it correct if the slider is ever resized.
        marker.anchorMin = new Vector2(normalizedTime, marker.anchorMin.y);
        marker.anchorMax = new Vector2(normalizedTime, marker.anchorMax.y);
        marker.anchoredPosition = new Vector2(0f, marker.anchoredPosition.y);

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
}
