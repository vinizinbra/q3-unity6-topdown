namespace Quantum
{
    // The first valid damaging hit against a full-health enemy applies 1 Rift Mark - one-time per
    // enemy (StatusEffects.FirstContactTriggered), not a cooldown. See
    // RiftMutationMarkUtility.TryFirstContact and docs/rift-mutations.md for the exact eligibility
    // rules (damage from another player, or a non-mutation-holder's hit, can close the window before
    // this player ever attacks). Plain-text Description, no live values to template in.
    public unsafe class FirstContactMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->HasFirstContactMutation = true;
        }
    }
}
