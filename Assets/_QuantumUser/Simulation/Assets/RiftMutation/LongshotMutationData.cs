namespace Quantum
{
    using Photon.Deterministic;

    // Mirror opposite of CloseQuartersMutationData - same two fields, tuned the other way on the
    // authored asset (+Far, -Near). See docs/rift-mutations.md.
    public unsafe class LongshotMutationData : RiftMutationData
    {
        public FP FarMultiplier = FP._1;
        public FP NearMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->FarDamageMultiplier = FPMath.Max(FP._0, stats->FarDamageMultiplier * FarMultiplier);
            stats->NearDamageMultiplier = FPMath.Max(FP._0, stats->NearDamageMultiplier * NearMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (FarMultiplier.AsFloat - 1f) * 100f,
            (NearMultiplier.AsFloat - 1f) * 100f
        };
    }
}
