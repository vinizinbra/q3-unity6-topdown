namespace Quantum
{
    // Hits against nearby enemies periodically apply Rift Mark - source-to-target distance
    // (FPVector3.Distance, deterministic simulation coordinates) checked against
    // ElementalReactionConfig.CloseRangeThreshold at hit-resolve time, per-target cooldown so
    // rapid-fire weapons can't apply one mark per shot. See
    // RiftMutationMarkUtility.TryCloseOrLongFracture and docs/rift-mutations.md. Plain-text
    // Description, no live values to template in.
    public unsafe class CloseFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasCloseFractureMutation = true;
        }
    }
}
