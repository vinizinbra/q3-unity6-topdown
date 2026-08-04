namespace Quantum
{
    // Large hits apply 1 Rift Mark - qualifies on EITHER ElementalReactionConfig.
    // HeavyHitDamageThreshold (flat) OR HeavyHitHealthPercentThreshold (percent of the target's own
    // MaxHealth), evaluated against one resolved hit's own damage only, never aggregated over time.
    // Per-target cooldown. See RiftMutationMarkUtility.TryHeavyFracture and docs/rift-mutations.md.
    // Plain-text Description, no live values to template in - the thresholds are global config, not
    // baked per-pick.
    public unsafe class HeavyFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasHeavyFractureMutation = true;
        }
    }
}
