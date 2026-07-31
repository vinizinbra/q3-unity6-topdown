namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Brute deals increased damage to Intimidated enemies - see
    // ProtectorAuraUtility.GetFearlessBonusMultiplier, folded into DamageUtility.ResolveOutgoingDamage.
    public unsafe partial class FearlessPassiveUpgradeData : PassiveUpgradeData
    {
        public FP BonusVsIntimidated = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            aura->FearlessBonusVsIntimidated += BonusVsIntimidated;
        }
    }
}
