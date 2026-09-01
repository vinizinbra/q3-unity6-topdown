namespace Quantum
{
    using Photon.Deterministic;

    // Rewards going untouched: every full second without losing health or shield builds damage, up
    // to a cap, and any real hit knocks it back to zero.
    //
    // The streak is a deterministic simulation counter (CharacterStats.SafeTimeSeconds, advanced by
    // MutationTimerUtility off f.DeltaTime) - never a View timer - so every client computes the same
    // bonus on the same tick. The bonus itself is derived from that counter on read rather than
    // stored, so "per full second, capped" lives in exactly one place.
    //
    // Reset is driven by the damage signals, which is what makes an Accessory-blocked hit leave the
    // streak intact: a block negates the hit entirely and never reaches them.
    public unsafe class PressureCookerMutationData : RiftMutationData
    {
        public FP DamagePerSecond = FP._0;
        public FP MaxDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->PressureCookerDamagePerSecond = FPMath.Max(stats->PressureCookerDamagePerSecond, DamagePerSecond);
            stats->PressureCookerMaxBonus = FPMath.Max(stats->PressureCookerMaxBonus, MaxDamageBonus);
            stats->SafeTimeSeconds = FP._0;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            DamagePerSecond.AsFloat * 100f,
            MaxDamageBonus.AsFloat * 100f
        };
    }
}
