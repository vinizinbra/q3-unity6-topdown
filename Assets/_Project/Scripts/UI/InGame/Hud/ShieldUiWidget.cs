using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Compact shield readout for one player - always bound externally via Initialize (e.g. by
// PartyHudWidget), same reasoning as HealthUiWidget.
public class ShieldUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private Slider shieldSlider;
    [SerializeField, Tooltip("Optional - shows Current/Max. Left unassigned, this feature is simply off.")]
    private TMP_Text shieldText;

    [SerializeField, Tooltip("Fill color for the bar. Left default (white) uses whatever color is already authored on the Slider's own fill Image.")]
    private Color normalFillColor = Color.cyan;

    [SerializeField, Tooltip("Only relevant for a TEMPORARY shield (Shield.TemporaryDuration > 0, e.g. Brute's Juggernaut) - once ExpirationRemaining drops to/below this many seconds, the fill pulses toward shieldWarningColor and shieldText appends a countdown, so it reads as \"about to disappear\" rather than just \"low.\" Never triggers for a plain persistent or classically recharging shield, since both always read TemporaryDuration 0.")]
    private float warningThreshold = 1.5f;
    [SerializeField] private Color shieldWarningColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField, Tooltip("Pulse speed while warning, in radians/sec.")]
    private float warningPulseSpeed = 6f;

    [SerializeField] private EntityRef _entityRef;

    private Image _fillImage;

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    public override void QStart(QuantumGame game)
    {
        if (shieldSlider != null && shieldSlider.fillRect != null)
            shieldSlider.fillRect.TryGetComponent(out _fillImage);
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        var frame = game.Frames.Predicted;

        if (frame.TryGet<Shield>(_entityRef, out var shield) == false || shield.Max <= FP._0)
            return;

        if (shieldSlider != null)
        {
            // Current can no longer exceed Max - every grant caps there now that Overshield is gone
            // (see ShieldUtility) - so this is a plain [0,1] fill with no above-full state to signal.
            shieldSlider.value = (shield.Current / shield.Max).AsFloat;
        }

        // Temporary shield (Brute's Juggernaut) about to run out - pulse the fill and append a
        // countdown instead of the plain color/label every other shield state uses. TemporaryDuration
        // is 0 for everything that never opted into expiration, so this never triggers for anyone
        // else.
        bool warning = shield.TemporaryDuration > FP._0 && shield.Current > FP._0
            && shield.ExpirationRemaining > FP._0 && shield.ExpirationRemaining.AsFloat <= warningThreshold;

        if (_fillImage != null)
        {
            if (warning == true)
            {
                float pulse = (Mathf.Sin(Time.time * warningPulseSpeed) + 1f) * 0.5f;
                _fillImage.color = Color.Lerp(normalFillColor, shieldWarningColor, pulse);
            }
            else
            {
                _fillImage.color = normalFillColor;
            }
        }

        if (shieldText != null)
        {
            shieldText.text = warning == true
                ? $"{Mathf.CeilToInt(shield.Current.AsFloat)}/{Mathf.CeilToInt(shield.Max.AsFloat)} ({shield.ExpirationRemaining.AsFloat:0.0}s)"
                : $"{Mathf.CeilToInt(shield.Current.AsFloat)}/{Mathf.CeilToInt(shield.Max.AsFloat)}";
        }
    }
}
