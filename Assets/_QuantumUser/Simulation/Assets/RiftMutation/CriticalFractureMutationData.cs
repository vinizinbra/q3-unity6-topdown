namespace Quantum
{
    // Critical hits from any source (weapon or skill) apply 1 Rift Mark, per-target cooldown - see
    // RiftMutationMarkUtility.EvaluateOnDamage/TryCriticalFracture and docs/rift-mutations.md. Shares
    // RiftMarkCooldownKey.CriticalFracture with the Weapon Perk of the same name so the two never
    // both stack from one crit. Plain-text Description, no live values to template in.
    public unsafe class CriticalFractureMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasCriticalFractureMutation = true;
        }
    }
}
