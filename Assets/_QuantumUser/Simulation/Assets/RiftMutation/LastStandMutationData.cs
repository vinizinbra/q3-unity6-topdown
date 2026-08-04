namespace Quantum
{
    // Taking a large hit (ElementalReactionConfig.LastStandThreshold, flat damage) releases a Rift
    // pulse that applies 1 Rift Mark to every nearby enemy, never the player themselves - per-player
    // internal cooldown (CharacterStats.LastStandCooldownRemaining), not per-target. See
    // RiftMutationMarkUtility.EvaluateLastStand and docs/rift-mutations.md. Plain-text Description,
    // no live values to template in.
    public unsafe class LastStandMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasLastStandMutation = true;
        }
    }
}
