namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Kai's Weapon Family Mastery - Sniper (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Sniper Damage.
    //  R3 "Deadeye": the first Sniper hit Kai lands against each enemy deals additional Damage, via the
    //  generic WeaponFamilyFirstHitTracker rather than a bespoke mark - naturally coexists with Kai's
    //  own First Strike Ascension without merging their state tracking.
    public unsafe partial class SniperMasteryData : WeaponFamilyMasteryData
    {
        [Tooltip("Deadeye (R3) - additional Damage on the first Sniper hit against each enemy.")]
        public FP DeadeyeDamageBonus = FP.FromString("0.30");

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<DeadeyeUpgrade>(entity, out var deadeye);
            deadeye->DamageBonus = DeadeyeDamageBonus;
        }
    }
}
