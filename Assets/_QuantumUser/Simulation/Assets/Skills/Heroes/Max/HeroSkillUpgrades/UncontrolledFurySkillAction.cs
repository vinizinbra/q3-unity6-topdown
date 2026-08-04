namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade for Berserk/Overdrive - while equipped, every kill landed during Overdrive
    // extends the current activation by PerKillExtension, up to MaxExtension total for that single
    // activation (see MaxOverdriveReactionSystem). Begin (re-)seeds a fresh
    // UncontrolledFuryExtension - AccumulatedExtension always starts at 0 for a new activation, the
    // cap doesn't carry over - End revokes it. The actual per-kill extension happens live in
    // MaxOverdriveReactionSystem, not here, same shape VendettaRushSkillAction/
    // RageOverdriveSkillAction already use for Begin-seeded, externally-consumed state.
    public unsafe partial class UncontrolledFurySkillAction : SkillActionData
    {
        public FP PerKillExtension = FP._0_20;
        public FP MaxExtension = 3;

        // {0} = PerKillExtension, {1} = MaxExtension - e.g. "Each kill during Overdrive extends it
        // by {0}s, up to {1}s per activation."
        protected override object[] DescriptionArgs => new object[] { PerKillExtension, MaxExtension };

        public UncontrolledFurySkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<UncontrolledFuryExtension>(filter.Entity, out var fury);
                fury->PerKillExtension = PerKillExtension;
                fury->MaxExtension = MaxExtension;
                fury->AccumulatedExtension = FP._0;
            }
            else
            {
                f.Remove<UncontrolledFuryExtension>(filter.Entity);
            }
        }
    }
}
