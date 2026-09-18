namespace Quantum
{
    using Photon.Deterministic;

    // A crit-commitment tradeoff, not an area effect: every NON-critical hit deals less damage, while
    // a critical hit's damage MULTIPLIER gets a flat add-on (e.g. base 2.0x -> 3.0x for the default
    // +1.0) - not a Crit Chance grant. Weak without enough Crit Chance to actually land crits often,
    // extremely valuable in a crit-focused build.
    //
    // Both halves are generic terms in DamageUtility's own crit resolution (ResolveOutgoingDamage's
    // non-crit return path, ResolveCriticalTerms' multiplier), so this affects any damage source that
    // already supports a normal critical hit - weapon, skill, explosion alike - with no hero or
    // source-specific handling anywhere.
    public unsafe class FocusedPowerMutationData : RiftMutationData
    {
        public FP NonCritDamagePenalty = FP._0;
        public FP CritDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->FocusedPowerNonCritDamagePenalty = FPMath.Max(stats->FocusedPowerNonCritDamagePenalty, NonCritDamagePenalty);
            stats->FocusedPowerCritDamageBonus = FPMath.Max(stats->FocusedPowerCritDamageBonus, CritDamageBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            NonCritDamagePenalty.AsFloat * 100f,
            CritDamageBonus.AsFloat * 100f
        };
    }
}
