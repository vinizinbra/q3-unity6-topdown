namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Zara's Weapon Weight Mastery - Light (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Light Weapon Damage.
    //  R3 "Full Tempo": while Flow is Active, her equipped Light weapon gains additional Fire Rate.
    //  Active is flipped by ZaraFlowUtility on Flow's own activation edge (see
    //  ConditionalWeaponFireRateBonus's own comment), read generically by WeaponSystem with no
    //  shot-counter/Flow-specific knowledge - works for Pistol, SMG, or any future Light weapon alike.
    public unsafe partial class ZaraLightMasteryData : WeaponWeightMasteryData
    {
        [Tooltip("Full Tempo (R3) - additional Light Weapon Fire Rate while Flow is Active.")]
        public FP FullTempoFireRateBonus = FP.FromString("0.20");

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<ConditionalWeaponFireRateBonus>(entity, out var fireRate);
            fireRate->Weight = WeaponWeight.Light;
            fireRate->FireRateBonus = FullTempoFireRateBonus;

            // Rebake Active immediately off Flow's CURRENT state - same "don't make her break and
            // rebuild Flow before the pick does anything" reasoning FasterTempoPassiveUpgradeData.Apply
            // already follows, since ApplyRank runs once at pick time, not on the next toggle edge.
            if (f.Unsafe.TryGetPointer<ZaraFlow>(entity, out var flow) == true)
            {
                fireRate->Active = flow->IsActive;
            }
        }
    }
}
