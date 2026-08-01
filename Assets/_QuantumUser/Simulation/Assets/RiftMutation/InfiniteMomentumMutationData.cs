namespace Quantum
{
    using Photon.Deterministic;

    // Faster Dash via the same rate-multiplier convention as DashCooldownUpgradeData, plus a flat
    // Shield cost consumed on every Dash activation - see RiftMutationReactionSystem.
    // OnSkillActivated for the drain-then-spill-to-Health-floored-at-1 side of this.
    public unsafe class InfiniteMomentumMutationData : RiftMutationData
    {
        public FP DashCooldownRateMultiplier = FP._1;
        public FP ShieldCost = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DashCooldownMultiplier = FPMath.Max(FP._0, stats->DashCooldownMultiplier * DashCooldownRateMultiplier);
            stats->DashShieldCost += ShieldCost;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (1f / DashCooldownRateMultiplier.AsFloat - 1f) * 100f,
            ShieldCost.AsFloat
        };
    }
}
