namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Zara's Element Mastery - Electric (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Electric Weapon Damage.
    //  R3 "High Voltage": applying Jolt grants Zara herself additional Fire Rate for a short duration -
    //  refreshes (never stacks) via the existing per-source Haste slots
    //  (StatusEffectUtility.ApplyHaste), triggered from the shared Jolt-landing hook in
    //  StatusEffectUtility.ApplyElementBaseline's Lightning case.
    public unsafe partial class ZaraElectricMasteryData : ElementMasteryData
    {
        [Tooltip("High Voltage (R3) - additional Fire Rate granted to Zara when she applies Jolt.")]
        public FP HighVoltageFireRateBonus = FP.FromString("0.15");

        [Tooltip("High Voltage (R3) - duration of the Fire Rate buff.")]
        public FP HighVoltageDuration = 2;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<SelfFireRateOnJoltUpgrade>(entity, out var highVoltage);
            highVoltage->FireRateBonus = HighVoltageFireRateBonus;
            highVoltage->Duration = HighVoltageDuration;
        }
    }
}
