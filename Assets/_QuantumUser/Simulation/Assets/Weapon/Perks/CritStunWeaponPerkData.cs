namespace Quantum
{
    using Photon.Deterministic;

    // Disruptor's signature - a crit briefly stuns its own target via StatusEffectUtility.ApplyStun.
    // Consumed by WeaponPerkReactionSystem.OnCriticalHit, same reaction hook Critical Rebound/
    // Bottomless Momentum/Explosive Crit share. Authored directly into WeaponDataAsset.BaseTraits
    // today (not offered through the roll pool), but shaped like any other WeaponPerkData so it
    // could be added to WeaponPerkPoolData later with no further work.
    public unsafe class CritStunWeaponPerkData : WeaponPerkData
    {
        public FP StunDuration = FP._0_50;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnCritReactions>(owner, out var reactions);
            reactions->CritStunDuration = FPMath.Max(reactions->CritStunDuration, StunDuration);
        }

        protected override object[] DescriptionArgs => new object[] { StunDuration.AsFloat };
    }
}
