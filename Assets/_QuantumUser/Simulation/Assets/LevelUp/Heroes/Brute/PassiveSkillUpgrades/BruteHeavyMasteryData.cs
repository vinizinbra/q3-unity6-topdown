namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Brute's Weapon Weight Mastery - Heavy (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Heavy Weapon Damage.
    //  R3 "Point Blank": additional Heavy Weapon Damage to nearby enemies, by distance from Brute
    //  himself - works for every weapon tagged Heavy (Sniper, Grenade Launcher, future Heavy weapons)
    //  regardless of pellet count/fire rate/magazine size, not just a Shotgun.
    public unsafe partial class BruteHeavyMasteryData : WeaponWeightMasteryData
    {
        [Tooltip("Point Blank (R3) - additional Heavy Weapon Damage to a target within Range of Brute.")]
        public FP PointBlankDamageBonus = FP.FromString("0.25");

        [Tooltip("Point Blank (R3) - distance from Brute a target must be within to qualify.")]
        public FP PointBlankRange = 4;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<PointBlankUpgrade>(entity, out var pointBlank);
            pointBlank->DamageBonus = PointBlankDamageBonus;
            pointBlank->Range = PointBlankRange;
        }
    }
}
