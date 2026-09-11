namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Hero Mastery - Weapon Family track (see docs/hero-mastery.md). Every hero has exactly one of
    // these lines (Family fixed per hero, e.g. Brute = Shotgun) and one ElementMasteryData line - both
    // drafted through the ordinary level-up Passive Upgrade pool like any other Ascension, riding on
    // the existing UpgradeHistory/PassiveUpgradeUtility rank machinery with zero new progression code.
    //
    // Sealed Apply(rank) - concrete Mastery lines override ApplyRank below instead, the same
    // "always-installed component + an optional rank-3-only extra" shape ConcussiveImpactSkillAction/
    // BoneBreakerSkillAction already use, so a hero-specific R3 Special is just another
    // f.AddOrGet<...>() call, never a branch in generic combat code.
    public abstract unsafe partial class WeaponFamilyMasteryData : PassiveUpgradeData
    {
        [Tooltip("Fixed per hero - which of the 6 weapon families this Mastery line affects.")]
        public WeaponFamily Family;

        [Tooltip("R1/R2/R3 total damage multiplier (0.15 = +15%). Cumulative totals, not stacked deltas - see PassiveUpgradeData.Apply(rank)'s own comment.")]
        public FP[] DamageMultiplierPerRank = { FP.FromString("0.15"), FP.FromString("0.30"), FP._0_50 };

        public sealed override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, MaxRank) - 1;

            f.AddOrGet<WeaponFamilyMastery>(entity, out var mastery);
            mastery->Family = Family;
            mastery->DamageMultiplier = DamageMultiplierPerRank[index];

            ApplyRank(f, entity, rank);
        }

        // Concrete Mastery lines override this for their own R3 Special - a no-op base so a line with
        // no bespoke component to install (none exist yet, but the hook stays symmetric with
        // ElementMasteryData) still compiles unchanged.
        protected virtual void ApplyRank(Frame f, EntityRef entity, int rank) { }
    }
}
