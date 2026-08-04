namespace Quantum
{
    // Hero Skill hits apply 1 Rift Mark, per-target cooldown so a persistent field/DoT/repeated
    // pulse doesn't reapply on every tick - see RiftMutationMarkUtility.TrySkillFracture and
    // docs/rift-mutations.md. Plain-text Description, no live values to template in.
    public unsafe class SkillFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasSkillFractureMutation = true;
        }
    }
}
