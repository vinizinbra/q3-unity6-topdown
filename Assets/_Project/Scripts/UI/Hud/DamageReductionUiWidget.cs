using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Shows the local player's current total damage reduction as a percentage - reads
// DamageUtility.ResolveDamageReduction directly (made public for exactly this) instead of
// re-deriving the CharacterStats/StatusEffects math here, so this can never drift out of sync with
// what actually reduces incoming damage. Combines whatever sources are currently stacking, e.g.
// Brute's own two: CharacterStats.DamageReduction, temporarily boosted for the whole Juggernaut Hero
// Skill channel (see JuggernautSkillData.Begin/End), and StatusEffects.DamageReductionAmount,
// refreshed every tick an ally stands in a Guardian-ascended Brute's Protector Aura (see
// ProtectorAuraSystem) - the player doesn't need to know which source it's coming from, just that
// they're currently tankier than normal. Self-hides via root whenever the combined reduction is 0.
// By default self-binds to local slot 0 (player 1), same as SkillCooldownUiWidget - see
// autoBindLocalPlayerOne.
public class DamageReductionUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text percentText;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        if (autoBindLocalPlayerOne)
            MyLocalPlayer.Instance.BindToSlot(0, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see the class comment above.
    public void DisableAutoBind()
    {
        autoBindLocalPlayerOne = false;
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

        // ResolveDamageReduction already no-ops to FP._1 (no reduction) for an entity with no
        // CharacterStats, so there's no separate existence check needed here.
        FP multiplier = DamageUtility.ResolveDamageReduction(frame, _entityRef);
        FP reduction = FP._1 - multiplier;

        if (reduction <= FP._0)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        if (percentText != null)
            percentText.text = $"-{Mathf.RoundToInt(reduction.AsFloat * 100f)}%";
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}
