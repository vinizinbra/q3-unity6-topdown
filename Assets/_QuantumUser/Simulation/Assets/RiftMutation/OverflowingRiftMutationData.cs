namespace Quantum
{
    // Applying Rift Mark to a target already at 2 stacks releases a small Rift pulse instead of
    // wasting the application - stacks stay clamped at MaxStacks (never a 3rd), duration still
    // refreshes, own dedicated cooldown (StatusEffects.OverflowingRiftCooldownRemaining) so
    // continuous application spam can't chain pulses. Deliberately restrained - never comparable in
    // strength to a full Rift Reaction. See RiftMarkApplicationUtility.TryTriggerOverflowingRift and
    // docs/rift-mutations.md. Plain-text Description, no live values to template in.
    public unsafe class OverflowingRiftMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasOverflowingRiftMutation = true;
        }
    }
}
