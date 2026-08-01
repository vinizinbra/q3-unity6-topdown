namespace Quantum
{
    using Photon.Deterministic;

    // Attacker-side range falloff - see DamageUtility.ResolveRangeDamageMultiplier and
    // CharacterStats.NearDamageMultiplier/FarDamageMultiplier. LongshotMutationData is the mirror
    // opposite; picking both is allowed and just partially cancels, no exclusivity system, same as
    // every other overlapping pair in this pool. See docs/rift-mutations.md.
    public unsafe class CloseQuartersMutationData : RiftMutationData
    {
        public FP NearMultiplier = FP._1;
        public FP FarMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->NearDamageMultiplier = FPMath.Max(FP._0, stats->NearDamageMultiplier * NearMultiplier);
            stats->FarDamageMultiplier = FPMath.Max(FP._0, stats->FarDamageMultiplier * FarMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (NearMultiplier.AsFloat - 1f) * 100f,
            (FarMultiplier.AsFloat - 1f) * 100f
        };
    }
}
