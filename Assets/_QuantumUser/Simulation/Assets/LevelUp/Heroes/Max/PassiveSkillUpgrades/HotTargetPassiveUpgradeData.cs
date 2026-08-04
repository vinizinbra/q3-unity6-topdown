namespace Quantum
{
    using Photon.Deterministic;

    // Fire Mastery trait - bonus Critical Chance against a currently-Burning target, read live
    // every crit roll (DamageUtility.ResolveOutgoingDamage) rather than baked into
    // CharacterStats.CriticalChance - Max's base Critical Chance must not be permanently modified.
    public unsafe partial class HotTargetPassiveUpgradeData : PassiveUpgradeData
    {
        public FP CriticalChanceBonusVsBurning = FP._0_10;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<ConditionalCriticalModifier>(entity, out var modifier);
            modifier->CriticalChanceBonusVsBurning = FPMath.Max(modifier->CriticalChanceBonusVsBurning, CriticalChanceBonusVsBurning);
        }
    }
}
