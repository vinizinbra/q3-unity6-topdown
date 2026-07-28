namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, Lux's deployed sentry boosts the shield recharge rate of
    // any ally standing within its Range (SentryAuraSystem, via StatusEffectUtility.ApplyShieldRegen)
    // - see SentryShieldAreaRateUpgrade, copied onto the spawned sentry by SpawnSentrySkillAction.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryAddShieldAreaRateSkillAction : SkillActionData
    {
        public FP ShieldRegenMultiplier = FP._1 + FP._0_50;

        public SentryAddShieldAreaRateSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = ShieldRegenMultiplier as-is (a raw multiplier, not a percent) - e.g. "...multiplies
        // nearby allies' shield recharge rate by {0}x within its range."
        protected override object[] DescriptionArgs => new object[] { ShieldRegenMultiplier };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryShieldAreaRateUpgrade>(filter.Entity, out var upgrade);
            upgrade->ShieldRegenMultiplier = ShieldRegenMultiplier;
        }
    }
}
