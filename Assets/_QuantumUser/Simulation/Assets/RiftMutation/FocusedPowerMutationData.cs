namespace Quantum
{
    using Photon.Deterministic;

    // Skill-side tradeoff - smaller area, bigger hit. Targets CharacterStats.AreaRadiusMultiplier/
    // SkillDamageMultiplier, the same two fields SkillAreaUpgradeData/SkillDamageUpgradeData already
    // scale individually. See docs/rift-mutations.md.
    public unsafe class FocusedPowerMutationData : RiftMutationData
    {
        public FP SkillAreaMultiplier = FP._1;
        public FP SkillDamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AreaRadiusMultiplier = FPMath.Max(FP._0, stats->AreaRadiusMultiplier * SkillAreaMultiplier);
            stats->SkillDamageMultiplier = FPMath.Max(FP._0, stats->SkillDamageMultiplier * SkillDamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (SkillAreaMultiplier.AsFloat - 1f) * 100f,
            (SkillDamageMultiplier.AsFloat - 1f) * 100f
        };
    }
}
