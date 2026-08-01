namespace Quantum
{
    using Photon.Deterministic;

    // Opposite tradeoff to HeavyArsenalMutationData - same fields, same shape, tuned the other way
    // on the authored asset (+Fire Rate, -Damage). See docs/rift-mutations.md.
    public unsafe class BulletStormMutationData : RiftMutationData
    {
        public FP FireRateMultiplier = FP._1;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AttackSpeedMultiplier = FPMath.Max(FP._0, stats->AttackSpeedMultiplier * FireRateMultiplier);
            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (FireRateMultiplier.AsFloat - 1f) * 100f,
            (DamageMultiplier.AsFloat - 1f) * 100f
        };
    }
}
