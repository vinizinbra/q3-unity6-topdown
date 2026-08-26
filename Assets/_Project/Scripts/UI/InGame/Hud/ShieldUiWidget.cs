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

        if (_fillImage != null)
            _fillImage.color = normalFillColor;

        if (shieldText != null)
            shieldText.text = $"{Mathf.CeilToInt(shield.Current.AsFloat)}/{Mathf.CeilToInt(shield.Max.AsFloat)}";
    }
}
