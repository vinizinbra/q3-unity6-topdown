namespace Quantum
{
    using Photon.Deterministic;

    // Crit-driven ability cycling: every Nth critical hit shaves a flat second off BOTH the Hero
    // Skill and the Dash, so a crit build cycles its kit noticeably faster.
    //
    // Counted, not timed. The progress counter lives on CharacterStats and is reset on trigger, so
    // the payoff is a deterministic function of how many crits actually landed rather than of a
    // hidden real-time internal cooldown - which also makes it reproducible in a replay and
    // inspectable in the debug dump.
    //
    // "Only valid offensive critical hits count" needs no check here: OnCriticalHit is only ever
    // fired from DamageUtility's own resolution path, which a replayed DoT tick never reaches.
    public unsafe class CriticalFocusMutationData : RiftMutationData
    {
        public byte CritsRequired = 3;
        public FP CooldownReduction = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->CritFocusThreshold = CritsRequired < 1 ? (byte)1 : CritsRequired;
            stats->CritFocusCooldownReduction = FPMath.Max(stats->CritFocusCooldownReduction, CooldownReduction);
            stats->CritFocusProgress = 0;
        }

        protected override object[] DescriptionArgs => new object[] { CritsRequired, CooldownReduction.AsFloat };
    }
}
