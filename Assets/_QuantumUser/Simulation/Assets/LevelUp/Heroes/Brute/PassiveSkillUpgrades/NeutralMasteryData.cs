namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Brute's Element Mastery - Neutral (see docs/hero-mastery.md). Neutral is intentionally weaker
    // than a standard Element Mastery - this line simply authors its own (lower) curve, no special-case
    // code anywhere reads "is this Neutral".
    //  R1/R2/R3: +10% / +20% / +40% Neutral Weapon Damage.
    //  R3 "Armored Assault": Neutral weapon hits gain +Knockback, AND Brute deals +Damage while in his
    //  EXISTING Juggernaut Charged state (no second Charge resource, no Stunned-target bonus - that's
    //  Brute's Stun-related Ascensions' own space).
    public unsafe partial class NeutralMasteryData : ElementMasteryData
    {
        [Tooltip("Armored Assault (R3) - additional Knockback on Neutral weapon hits.")]
        public FP NeutralKnockbackBonus = FP.FromString("0.30");

        [Tooltip("Armored Assault (R3) - additional Brute-wide outgoing Damage while Juggernaut Charged.")]
        public FP ChargedDamageBonus = FP.FromString("0.30");

        // Neutral's own (weaker) curve - authored explicitly by BruteAscensionAssetGenerator on every
        // run, same as every other ranked field on this class; the inherited 15/30/50 default here is
        // only ever what a brand-new, never-yet-generated instance shows in the Inspector.
        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<NeutralWeaponKnockbackBonusUpgrade>(entity, out var knockback);
            knockback->KnockbackBonus = NeutralKnockbackBonus;

            f.AddOrGet<ChargedDamageBonusUpgrade>(entity, out var charged);
            charged->DamageMultiplier = ChargedDamageBonus;
        }
    }
}
