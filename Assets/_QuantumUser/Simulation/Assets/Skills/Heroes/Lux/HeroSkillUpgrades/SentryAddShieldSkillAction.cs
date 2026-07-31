namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, Lux's deployed sentry spawns with a Shield (see
    // SpawnSentrySkillAction.ApplyShieldUpgrade) - a vanilla sentry has none, same optional-component
    // pattern every other shielded entity in the game already follows.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryAddShieldSkillAction : SkillActionData
    {
        public FP Max = 20;
        public FP RechargeDelay = 3;
        public FP RechargeRate = 5;

        public SentryAddShieldSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryShieldUpgrade>(filter.Entity, out var upgrade);
            upgrade->Max = Max;
            upgrade->RechargeDelay = RechargeDelay;
            upgrade->RechargeRate = RechargeRate;
        }
    }
}
