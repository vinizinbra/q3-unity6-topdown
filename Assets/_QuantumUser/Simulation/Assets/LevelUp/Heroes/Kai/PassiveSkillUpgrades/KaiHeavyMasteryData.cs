namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Kai's Weapon Weight Mastery - Heavy (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Heavy Weapon Damage.
    //  R3 "Deadeye": the first Heavy-weapon hit Kai lands against each enemy deals additional Damage,
    //  via the generic WeaponWeightFirstHitTracker rather than a bespoke mark - per target/per owner/
    //  per weight, so switching between different Heavy weapons still counts as the same opener against
    //  a given enemy. Naturally coexists with Kai's own First Strike Ascension without merging state.
    public unsafe partial class KaiHeavyMasteryData : WeaponWeightMasteryData
    {
        [Tooltip("Deadeye (R3) - additional Damage on the first Heavy-weapon hit against each enemy.")]
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
