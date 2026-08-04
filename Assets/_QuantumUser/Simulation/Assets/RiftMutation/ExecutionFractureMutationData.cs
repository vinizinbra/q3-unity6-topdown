namespace Quantum
{
    // Hitting enemies already below ElementalReactionConfig.ExecutionHealthThreshold (25% MVP
    // default) applies 1 Rift Mark - checked against health BEFORE this hit's own damage, per-target
    // cooldown. See RiftMutationMarkUtility.TryExecutionFracture and docs/rift-mutations.md.
    // Plain-text Description, no live values to template in.
    public unsafe class ExecutionFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasExecutionFractureMutation = true;
        }
    }
}
