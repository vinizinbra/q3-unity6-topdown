namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - adds RangeBonus on top of SpawnSentrySkillAction's own authored Range the
    // next time Lux deploys a sentry (see SentryRangeUpgrade) - an already-deployed sentry keeps
    // whatever range it spawned with, same convention every other upgrade in this session follows.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryIncreaseRangeSkillAction : SkillActionData
    {
        public FP RangeBonus = 3;

        public SentryIncreaseRangeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryRangeUpgrade>(filter.Entity, out var upgrade);
            upgrade->RangeBonus = RangeBonus;
        }
    }
}
