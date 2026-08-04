namespace Quantum
{
    // Enemies that remain within ElementalReactionConfig.FracturedPresenceRadius of this player for
    // FracturedPresenceExposureTime become Rift-marked, tracked per (player, enemy) pair on the
    // enemy's own StatusEffects.FracturedPresenceExposedBy/ExposureTime slots, per-target cooldown
    // after applying. See RiftMutationMarkUtility.TickFracturedPresence and docs/rift-mutations.md.
    // Plain-text Description, no live values to template in.
    public unsafe class FracturedPresenceMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasFracturedPresenceMutation = true;
        }
    }
}
