namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - allies inside the Protector Aura gain Damage Reduction - see
    // ProtectorAuraSystem.ApplyToAllies/StatusEffectUtility.ApplyDamageReduction.
    public unsafe partial class GuardianPassiveUpgradeData : PassiveUpgradeData
    {
        public FP AllyDamageReductionAmount = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            aura->AllyDamageReductionAmount = AllyDamageReductionAmount;
        }
    }
}
