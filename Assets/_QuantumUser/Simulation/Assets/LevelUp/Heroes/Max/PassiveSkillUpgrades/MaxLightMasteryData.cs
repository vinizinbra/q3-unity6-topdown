namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Max's Weapon Weight Mastery - Light (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Light Weapon Damage.
    //  R3 "Vendetta": Light weapons deal additional Damage to whichever enemy currently carries Max's
    //  own existing Vendetta mark (RevengeMark) - no dependency on fire rate/magazine size/shot count/
    //  semi-auto vs automatic, so it works for Pistol, SMG, or any future Light weapon automatically.
    public unsafe partial class MaxLightMasteryData : WeaponWeightMasteryData
    {
        [Tooltip("Vendetta (R3) - additional Light Weapon Damage against Max's own Vendetta-marked enemy.")]
        public FP VendettaDamageBonus = FP.FromString("0.25");

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<VendettaUpgrade>(entity, out var vendetta);
            vendetta->DamageBonus = VendettaDamageBonus;
        }
    }
}
