namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, Lux's deployed sentry explodes when it dies (see
    // SentryOverloadUpgrade, copied onto the spawned sentry by
    // SpawnSentrySkillAction.ApplyOverloadUpgrade). Fully independent from the enemy
    // MarkExplosiveDeath/ExplodeOnDeath kill-chain mechanic - its own Radius/Damage here, not
    // RuntimeConfig.ExplodeOnDeathConfig (DamageUtility.TrySentryOverload).
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryAddOverloadSkillAction : SkillActionData
    {
        public FP Damage = 20;
        public FP Radius = 3;

        public SentryAddOverloadSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryOverloadUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->Radius = Radius;
            upgrade->Source = this;
        }
    }
}
