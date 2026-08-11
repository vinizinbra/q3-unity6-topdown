namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - rewards accurate Bunny Bomb placement: enemies inside the inner
    // InnerRadiusFraction of any of Pixie's explosions take bonus damage (see
    // DemolitionMasteryUtility.ApplyProximityEffects, never baked into CharacterStats). At rank 3,
    // those same inner-zone hits also apply strong knockback - this replaces the old standalone
    // Concussive Force ascension (folded in rather than kept separate, see DirectHitUpgrade.qtn).
    // Each rank SETS the total damage bonus (not additive across ranks).
    public unsafe partial class DirectHitPassiveUpgradeData : PassiveUpgradeData
    {
        public FP InnerRadiusFraction = FP.FromString("0.35");
        public FP[] DamageMultiplierBonus = { FP.FromString("0.30"), FP.FromString("0.50"), FP.FromString("0.75") };

        // Ported from the deleted standalone Concussive Force ascension - only takes effect at rank 3.
        public FP KnockbackForce = 8;
        public FP KnockbackUpwardForce = 2;
        public FP KnockbackEliteMultiplier = FP.FromString("0.4");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<DirectHitUpgrade>(entity, out var upgrade);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            upgrade->InnerRadiusFraction = InnerRadiusFraction;
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->HasKnockback = rank >= 3;
            upgrade->KnockbackForce = KnockbackForce;
            upgrade->KnockbackUpwardForce = KnockbackUpwardForce;
            upgrade->KnockbackEliteMultiplier = KnockbackEliteMultiplier;
        }
    }
}
