namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Max's Weapon Family Mastery - Pistol (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Pistol Damage.
    //  R3 "Vendetta": Pistols deal additional Damage to whichever enemy currently carries Max's own
    //  existing Vendetta mark (RevengeMark) - no dependency on fire rate/magazine size/shot count/
    //  semi-auto vs automatic, so it works for any future Pistol variant (revolvers, machine pistols,
    //  burst pistols, heavy pistols) automatically.
    public unsafe partial class PistolMasteryData : WeaponFamilyMasteryData
    {
        [Tooltip("Vendetta (R3) - additional Pistol Damage against Max's own Vendetta-marked enemy.")]
        public FP VendettaDamageBonus = FP.FromString("0.25");

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<VendettaPistolUpgrade>(entity, out var vendetta);
            vendetta->DamageBonus = VendettaDamageBonus;
        }
    }
}
