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

    [SerializeField, Tooltip("Fill color while Current <= Max (the normal case). Left default (white) uses whatever color is already authored on the Slider's own fill Image.")]
    private Color normalFillColor = Color.cyan;
    [SerializeField, Tooltip("Fill color while Current > Max (Overshield - see ShieldUtility.ApplyOvershield, e.g. Zara's Encore/Restorative Beat). The Slider's own value clamps to its [0,1] range regardless (so the bar itself always reads as 'full' once at or above Max - it can't stretch past its own frame without a dedicated overshield segment), so this color swap is what actually signals the overshielded state; shieldText still shows the true Current/Max numbers either way.")]
    private Color overshieldFillColor = new Color(1f, 0.85f, 0.25f);

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

        bool isOvershielded = shield.Current > shield.Max;

        if (shieldSlider != null)
        {
            // Current can legitimately exceed Max (Overshield) - Slider.value clamps to its own
            // [0,1] range either way, so this can't grow the bar past "full," only signal the state
            // via color (see _fillImage below).
            shieldSlider.value = (shield.Current / shield.Max).AsFloat;
        }

        if (_fillImage != null)
            _fillImage.color = isOvershielded ? overshieldFillColor : normalFillColor;

        if (shieldText != null)
            shieldText.text = $"{Mathf.CeilToInt(shield.Current.AsFloat)}/{Mathf.CeilToInt(shield.Max.AsFloat)}";
    }
}
