namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - permanently increases Lux's deployed sentry's own barrels' fire rate (see
    // SentryFireRateUpgrade, baked directly into each barrel's Weapon.FireCooldown by
    // SpawnSentrySkillAction.ApplyFireRateUpgrade). Distinct from SentryAddFireRateSkillAction, which
    // instead buffs nearby ALLIES via a temporary Haste status effect - this only speeds up the
    // sentry's own guns. An already-deployed sentry keeps whatever fire rate it spawned with.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryIncreaseFireRateSkillAction : SkillActionData
    {
        public FP AttackSpeedMultiplier = FP._1 + FP._0_25;

        public SentryIncreaseFireRateSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = AttackSpeedMultiplier expressed as the percent boost over baseline (1.0 = +0%) - e.g.
        // "Permanently increases the deployed sentry's own fire rate by {0}%."
        protected override object[] DescriptionArgs => new object[] { (AttackSpeedMultiplier - FP._1) * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryFireRateUpgrade>(filter.Entity, out var upgrade);
            upgrade->AttackSpeedMultiplier = AttackSpeedMultiplier;
        }
    }
}
