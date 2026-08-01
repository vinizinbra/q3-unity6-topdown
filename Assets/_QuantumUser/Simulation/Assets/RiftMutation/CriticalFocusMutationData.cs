namespace Quantum
{
    using Photon.Deterministic;

    // Merged mutation - flat cooldown seconds refunded on BOTH Hero Skill and Dash per crit (an
    // earlier design split this into two independent picks, Critical Focus/Critical Reflexes;
    // merged into one since Rift Mutations don't stack, so two overlapping picks made less sense
    // here than for the small-increment Global Upgrade pool). See
    // RiftMutationReactionSystem.OnCriticalHit and docs/rift-mutations.md.
    public unsafe class CriticalFocusMutationData : RiftMutationData
    {
        public FP CooldownReduction = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->CritSkillCooldownReduction += CooldownReduction;
        }

        protected override object[] DescriptionArgs => new object[] { CooldownReduction.AsFloat };
    }
}
