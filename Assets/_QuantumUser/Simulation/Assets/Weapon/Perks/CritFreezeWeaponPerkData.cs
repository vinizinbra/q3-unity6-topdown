namespace Quantum
{
    using Photon.Deterministic;

    // Frostbite's signature (Deep Freeze) - a crit freezes its own target via
    // StatusEffectUtility.ApplyFreeze: the true Ice hard-CC, so the Freeze visuals, tier
    // FreezeDurationMultiplier and anti-permafreeze FreezeRecoveryRemaining all apply - unlike
    // CritStunWeaponPerkData (Disruptor), which it otherwise mirrors. Consumed by
    // WeaponPerkReactionSystem.OnCriticalHit.
    public unsafe class CritFreezeWeaponPerkData : WeaponPerkData
    {
        public FP FreezeDuration = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnCritReactions>(owner, out var reactions);
            reactions->CritFreezeDuration = FPMath.Max(reactions->CritFreezeDuration, FreezeDuration);
        }

        protected override object[] DescriptionArgs => new object[] { FreezeDuration.AsFloat };
    }
}
