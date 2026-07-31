namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every heal the owner's speaker applies is boosted by
    // HealBonus - see HealUtility.ResolveHealMultiplier, checked live on every heal application.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill produces" upgrade this session: re-granting fresh (idempotent) every activation and
    // never removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class IncreaseHealSkillAction : SkillActionData
    {
        public FP HealBonus = FP._0_50;

        public IncreaseHealSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = HealBonus as a percent - e.g. "Increases the speaker's heal pulse by {0}% while
        // equipped."
        protected override object[] DescriptionArgs => new object[] { HealBonus * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<IncreaseHealUpgrade>(filter.Entity, out var upgrade);
            upgrade->HealBonus = HealBonus;
        }
    }
}
