namespace Quantum
{
    // Hits against distant enemies periodically apply Rift Mark - mirror of Close Fracture, checked
    // against ElementalReactionConfig.LongRangeThreshold instead, same deterministic-distance/
    // per-target-cooldown shape. See RiftMutationMarkUtility.TryCloseOrLongFracture and
    // docs/rift-mutations.md. Plain-text Description, no live values to template in.
    public unsafe class LongFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasLongFractureMutation = true;
        }
    }
}
