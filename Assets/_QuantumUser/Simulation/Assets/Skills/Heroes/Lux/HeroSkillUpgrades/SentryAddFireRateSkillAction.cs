namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, Lux's deployed sentry buffs the attack speed of any ally
    // standing within its Range (SentryAuraSystem, via StatusEffectUtility.ApplyHaste) - see
    // SentryFireRateAuraUpgrade, copied onto the spawned sentry by SpawnSentrySkillAction.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryAddFireRateSkillAction : SkillActionData
    {
        public FP AttackSpeedMultiplier = FP._1 + FP._0_20;

        public SentryAddFireRateSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = AttackSpeedMultiplier expressed as the percent boost over baseline (1.0 = +0%) - e.g.
        // "...hastes nearby allies' attack speed by {0}% within its range."
        protected override object[] DescriptionArgs => new object[] { (AttackSpeedMultiplier - FP._1) * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryFireRateAuraUpgrade>(filter.Entity, out var upgrade);
            upgrade->AttackSpeedMultiplier = AttackSpeedMultiplier;
        }
    }
}
