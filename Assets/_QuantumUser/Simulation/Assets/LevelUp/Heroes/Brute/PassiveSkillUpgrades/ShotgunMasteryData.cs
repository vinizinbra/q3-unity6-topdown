namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Brute's Weapon Family Mastery - Shotgun (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Shotgun Damage.
    //  R3 "Point Blank": additional Shotgun Damage to nearby enemies, by distance from Brute himself -
    //  works for every weapon tagged Shotgun regardless of pellet count/fire rate/magazine size.
    public unsafe partial class ShotgunMasteryData : WeaponFamilyMasteryData
    {
        [Tooltip("Point Blank (R3) - additional Shotgun Damage to a target within Range of Brute.")]
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
