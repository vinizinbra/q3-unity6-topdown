namespace Quantum
{
    using Photon.Deterministic;

    // Rewards clearing the environment: destroying ANY Breakable (barrel, crate, breakable wall - no
    // distinction) grants the destroyer a short burst of Move Speed and Fire Rate.
    //
    // Reacts to the generic OnDestructibleBroken signal (Breakable.qtn), not any one specific prefab
    // or prop type - RiftMutationReactionSystem.OnDestructibleBroken fires this for any current or
    // future Breakable.
    //
    // The payoff rides the generic timed-buff slots, so destroying another barrel while the buff is
    // still up REFRESHES its duration rather than stacking it.
    public unsafe class ScavengerRushMutationData : RiftMutationData
    {
        public FP BuffDuration = FP._0;
        public FP MoveSpeedBonus = FP._0;
        public FP FireRateBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->ScavengerBuffDuration = FPMath.Max(stats->ScavengerBuffDuration, BuffDuration);
            stats->ScavengerMoveSpeedBonus = FPMath.Max(stats->ScavengerMoveSpeedBonus, MoveSpeedBonus);
            stats->ScavengerFireRateBonus = FPMath.Max(stats->ScavengerFireRateBonus, FireRateBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            MoveSpeedBonus.AsFloat * 100f,
            FireRateBonus.AsFloat * 100f,
            BuffDuration.AsFloat
        };
    }
}
