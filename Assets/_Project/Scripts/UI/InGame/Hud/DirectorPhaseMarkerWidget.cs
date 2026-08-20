using UnityEngine;
using UnityEngine.UI;

// Sits on DirectorTimelineUiWidget's markerPrefab - explicit serialized reference to the phase icon
// Image, instead of GetComponentInChildren<Image> guessing which Image under the prefab is the icon
// (the marker's own tick/line visual is often an Image too, so a blind search could grab that one).
//
// Three run-relative states, driven each frame by DirectorTimelineUiWidget from the current phase:
//   Before  - the run hasn't reached this phase yet (beforeColor).
//   Reached - the run is currently in this phase (reachedColor + an idle scale wiggle).
//   Passed  - the run has moved past this phase (passedColor).
public class DirectorPhaseMarkerWidget : MonoBehaviour
{
    public enum MarkerState { Before, Reached, Passed }

    [SerializeField] private Image icon;

    [Header("State colors")]
    [SerializeField, Tooltip("Graphics (tick line, icon, background, etc.) tinted per state. Leave empty to disable recoloring.")]
    private Graphic[] colorTargets;
    [SerializeField] private Color beforeColor = Color.white;
    [SerializeField] private Color reachedColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color passedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Reached idle wiggle (scale)")]
    [SerializeField, Tooltip("What scales while Reached. Defaults to this marker's own transform if left empty.")]
    private Transform wiggleTarget;
    [SerializeField, Tooltip("Peak scale offset, e.g. 0.12 = pulses between 88% and 112%.")]
    private float wiggleScaleAmount = 0.12f;
    [SerializeField, Tooltip("Pulses per second.")]
    private float wiggleFrequency = 2f;

    // Set by DirectorTimelineUiWidget.SpawnMarker - the index into SurvivalConfig.Phases of the phase
    // this marker represents, so the widget can compare it against Global.CurrentPhaseIndex.
    public int PhaseIndex { get; set; }

    public Image Icon => icon;

    // Start invalid so the first SetState always applies, regardless of which value it is.
    private MarkerState _state = (MarkerState)(-1);
    private Vector3 _baseScale;
    private bool _baseCaptured;

    private void Awake()
    {
        CaptureBase();
    }

    // Idempotent - only re-tints / touches scale when the state actually changes.
    public void SetState(MarkerState state)
    {
        if (_state == state)
            return;

        _state = state;
        ApplyColor();

        // Snap scale back to rest whenever we leave Reached; the wiggle in Update owns it while Reached.
        if (state != MarkerState.Reached)
            RestoreScale();
    }

    private void Update()
    {
        if (_state != MarkerState.Reached)
            return;

        CaptureBase();

        float w = 1f + Mathf.Sin(Time.unscaledTime * wiggleFrequency * Mathf.PI * 2f) * wiggleScaleAmount;
        wiggleTarget.localScale = _baseScale * w;
    }

    private void ApplyColor()
    {
        Color c = _state == MarkerState.Before ? beforeColor
            : _state == MarkerState.Reached ? reachedColor
            : passedColor;

        if (colorTargets == null)
            return;

        for (int i = 0; i < colorTargets.Length; i++)
        {
            if (colorTargets[i] != null)
                colorTargets[i].color = c;
        }
    }

    private void RestoreScale()
    {
        if (_baseCaptured && wiggleTarget != null)
            wiggleTarget.localScale = _baseScale;
    }

    private void CaptureBase()
    {
        if (_baseCaptured)
            return;

        if (wiggleTarget == null)
            wiggleTarget = transform;

        _baseScale = wiggleTarget.localScale;
        _baseCaptured = true;
    }
}
