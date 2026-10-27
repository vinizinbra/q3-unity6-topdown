namespace Quantum
{
    using Photon.Deterministic;

    // +DamageBonus ALL damage, Max Health x HealthMultiplier. Authored IncompatibleWith Glass Core,
    // which also halves Max Health - the two together would be a near-certain death, not a build.
    public unsafe class DeadlyCurseMutationData : RiftMutationData
    {
        public FP DamageBonus = FP.FromString("0.60");
        public FP HealthMultiplier = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DamageMultiplier = FPMath.Max(FP._0, stats->DamageMultiplier * (FP._1 + DamageBonus));
            stats->MaxHealthMultiplier = FPMath.Max(FP._0, stats->MaxHealthMultiplier * HealthMultiplier);
            CharacterSystem.RefreshMaxHealth(f, entity);
        }

        protected override object[] DescriptionArgs => new object[] { DamageBonus.AsFloat * 100f, (HealthMultiplier.AsFloat - 1f) * 100f };
    }
}
