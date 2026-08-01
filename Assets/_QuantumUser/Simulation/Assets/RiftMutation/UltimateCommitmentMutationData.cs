namespace Quantum
{
    using Photon.Deterministic;

    // Skill-side tradeoff mirror of HeavyArsenalMutationData - SkillCooldownMultiplier is a rate
    // (higher = faster, see StatUtility.GetSkillCooldown's baseCooldown / multiplier convention),
    // so multiplying it down by SkillCooldownRateMultiplier doubles the effective cooldown duration
    // for a 0.5 tuning even though the field itself shrinks, not grows. See
    // docs/rift-mutations.md.
    public unsafe class UltimateCommitmentMutationData : RiftMutationData
    {
        public FP SkillDamageMultiplier = FP._1;
        public FP SkillCooldownRateMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->SkillDamageMultiplier = FPMath.Max(FP._0, stats->SkillDamageMultiplier * SkillDamageMultiplier);
            stats->SkillCooldownMultiplier = FPMath.Max(FP._0, stats->SkillCooldownMultiplier * SkillCooldownRateMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (SkillDamageMultiplier.AsFloat - 1f) * 100f,
            (1f / SkillCooldownRateMultiplier.AsFloat - 1f) * 100f
        };
    }
}
