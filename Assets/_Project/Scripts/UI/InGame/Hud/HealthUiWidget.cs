using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Compact health readout for one player - always bound externally via Initialize (e.g. by
// PartyHudWidget), unlike SkillCooldownUiWidget/etc. there's no "player 1's own HUD" legacy usage
// to preserve here, so no self-binding default.
public class HealthUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private Slider healthSlider;
    [SerializeField, Tooltip("Optional - shows CurrentHealth/MaxHealth. Left unassigned, this feature is simply off.")]
    private TMP_Text healthText;

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

        if (frame.TryGet<Health>(_entityRef, out var health) == false || health.MaxHealth <= FP._0)
            return;

        if (healthSlider != null)
            healthSlider.value = (health.CurrentHealth / health.MaxHealth).AsFloat;

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(health.CurrentHealth.AsFloat)}/{Mathf.CeilToInt(health.MaxHealth.AsFloat)}";
    }
}
