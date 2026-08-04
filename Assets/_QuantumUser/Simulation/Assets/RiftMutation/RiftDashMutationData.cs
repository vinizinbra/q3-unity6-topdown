namespace Quantum
{
    // Dashing through an enemy applies 1 Rift Mark, once per enemy per dash - see DashSkillData's own
    // Rift Dash overlap sweep (gated on CharacterStats.HasRiftDashMutation) and RiftDashMarkTracker
    // (LevelUp... no, RiftDashMarkTracker.qtn - the per-dash dedupe array), plus docs/rift-mutations.md.
    // The universal dash itself carries this, not a per-hero ascension slot. Plain-text Description,
    // no live values to template in.
    public unsafe class RiftDashMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasRiftDashMutation = true;
        }
    }
}
