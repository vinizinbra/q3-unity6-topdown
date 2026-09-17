namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Pixie's Weapon Weight Mastery - Heavy (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Heavy Weapon Damage.
    //  R3 "Heavy Hit Area Expansion" - every Heavy-weapon attack's effective hit area grows by
    //  ExtraRadius (HeavyHitAreaExpansionUpgrade), added onto an attack that already has an area
    //  (AreaHitData.Detonate) or granted fresh around the impact point for one that didn't
    //  (HeavyHitAreaUtility.TryExpandSingleTargetHit) - the primary target is always excluded from that
    //  expansion since it already took the shot's own hit, so nobody is ever double-hit. Reads
    //  identically for every Heavy weapon (Sniper, Grenade Launcher, future Heavy weapons) rather than
    //  depending on that weapon's own projectile behavior.
    public unsafe partial class PixieHeavyMasteryData : WeaponWeightMasteryData
    {
        [Tooltip("Heavy Hit Area Expansion (R3) - flat additive hit-area radius bonus for every Heavy-weapon attack.")]
        public FP ExtraRadius = 2;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<HeavyHitAreaExpansionUpgrade>(entity, out var areaExpansion);
            areaExpansion->ExtraRadius = ExtraRadius;
        }
    }
}
