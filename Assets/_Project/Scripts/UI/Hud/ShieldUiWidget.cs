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

    [SerializeField] private EntityRef _entityRef;

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    public override void QStart(QuantumGame game)
    {
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
            shieldSlider.value = (shield.Current / shield.Max).AsFloat;

        if (shieldText != null)
            shieldText.text = $"{Mathf.CeilToInt(shield.Current.AsFloat)}/{Mathf.CeilToInt(shield.Max.AsFloat)}";
    }
}
