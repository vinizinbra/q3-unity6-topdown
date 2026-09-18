namespace Quantum
{
    using Photon.Deterministic;

    // Passive co-op build synergy that needs no combat coordination: a flat All Damage bonus for its
    // owner, plus more for every OTHER Raider in the run who ALSO owns it - counted by ownership
    // alone (RiftMutationPicks), never by proximity, being alive, or fighting the same enemy.
    //
    // Deliberately resolved LIVE every hit (MutationModifierUtility.ResolveLiveDamageMultiplier)
    // rather than baked into a flat multiplier at pick time - a teammate picking this up later has to
    // retroactively raise everyone who already holds it too, and this is the only way that stays true
    // without every existing owner re-applying their pick.
    public unsafe class BroMutationData : RiftMutationData
    {
        public FP BaseDamageBonus = FP._0;
        public FP PerOtherOwnerDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->BroMutationBaseDamageBonus = FPMath.Max(stats->BroMutationBaseDamageBonus, BaseDamageBonus);
            stats->BroMutationPerOwnerDamageBonus = FPMath.Max(stats->BroMutationPerOwnerDamageBonus, PerOtherOwnerDamageBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            BaseDamageBonus.AsFloat * 100f,
            PerOtherOwnerDamageBonus.AsFloat * 100f
        };
    }
}
