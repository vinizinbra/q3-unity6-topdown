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
    [SerializeField, Tooltip("Optional - the slider's fill, recoloured to lowHealthColor below lowHealthThreshold so the bar alone reads as \"in trouble\" (e.g. teammate slots with no number). Left unassigned, this feature is simply off.")]
    private Graphic healthFill;
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.3f;
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.23f, 0.23f);

    [SerializeField] private EntityRef _entityRef;

    private Color _normalFillColor;

    private void Awake()
    {
        if (healthFill != null)
            _normalFillColor = healthFill.color;
    }

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

        float ratio = (health.CurrentHealth / health.MaxHealth).AsFloat;

        if (healthSlider != null)
            healthSlider.value = ratio;

        if (healthFill != null)
            healthFill.color = ratio <= lowHealthThreshold ? lowHealthColor : _normalFillColor;

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(health.CurrentHealth.AsFloat)}/{Mathf.CeilToInt(health.MaxHealth.AsFloat)}";
    }
}
