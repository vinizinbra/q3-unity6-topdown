namespace Quantum
{
    // Doesn't touch a stat directly - flips CharacterStats.ShieldBreakGrantsDashCharge, consumed by
    // RiftMutationReactionSystem.OnShieldBroken (Shield.qtn's new OnShieldBroken signal, fired from
    // DamageUtility.AbsorbWithShield the exact tick Shield.Current crosses from >0 to <=0). Refills
    // one Dash charge (CurrentStacks, capped at MaxStacks) rather than raising MaxStacks - a proc,
    // not a permanent capacity increase like Dash Charge (Global Upgrade). Plain-text Description,
    // no live values to template in - see docs/rift-mutations.md.
    public unsafe class ShieldBreakerMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->ShieldBreakGrantsDashCharge = true;
        }
    }
}
