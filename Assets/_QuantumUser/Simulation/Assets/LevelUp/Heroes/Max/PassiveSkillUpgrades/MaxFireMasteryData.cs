namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Max's Element Mastery - Fire (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Fire Weapon Damage.
    //  R3 "Infernal Rage": Fire weapons deal additional Damage while Max is in his EXISTING Overdrive
    //  activation (component PRESENCE of RageOverdrive, see that qtn's own comment) - does NOT
    //  generate extra Rage, and does not duplicate Wildfire/Flashpoint.
    public unsafe partial class MaxFireMasteryData : ElementMasteryData
    {
        [Tooltip("Infernal Rage (R3) - additional Fire weapon Damage while Overdrive is active.")]
        public FP InfernalRageDamageBonus = FP.FromString("0.30");

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<InfernalRageUpgrade>(entity, out var infernalRage);
            infernalRage->DamageBonus = InfernalRageDamageBonus;
        }
    }
}
