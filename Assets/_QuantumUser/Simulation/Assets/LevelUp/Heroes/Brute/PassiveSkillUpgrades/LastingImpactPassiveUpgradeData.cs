namespace Quantum
{
    using Photon.Deterministic;

    // Knockback Mastery Hero Trait - a Stun Brute himself causes lasts longer, read live inside
    // StatusEffectUtility.ApplyStun once it knows the stunning owner - see Heroes/Brute/
    // KnockbackMastery.qtn.
    public unsafe partial class LastingImpactPassiveUpgradeData : PassiveUpgradeData
    {
        public FP DurationMultiplierBonus = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<LastingImpactUpgrade>(entity, out var upgrade);
            upgrade->DurationMultiplierBonus += DurationMultiplierBonus;
        }
    }
}
