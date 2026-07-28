namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every enemy Brutus launches with a discharge deals Damage
    // to itself and rolls StunChance for RuntimeConfig.EffectConfig.StunDuration the moment it
    // touches ground again - see JuggernautLandingImpactUpgrade, JuggernautLaunched and
    // JuggernautLandingImpactSystem. Distinct from JuggernautDischargeDamageSkillAction (damage on
    // launch) and JuggernautEndExplosionSkillAction (damage on channel expiry) - this one fires on
    // landing specifically. Only Duration is read from EffectConfig (same reasoning as every other
    // status effect) - Damage/StunChance stay authored here since they're this specific skill's own
    // tuning, not a property of Stun itself.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautLandingImpactSkillAction : SkillActionData
    {
        public FP Damage = 10;
        public FP StunChance = FP._0_50;

        public JuggernautLandingImpactSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Damage, {1} = StunChance as a percent - e.g. "Deals {0} damage and has a {1}% chance
        // to stun enemies when they land after being launched by a discharge." Duration comes from
        // EffectConfig, not a local field, so it isn't templated here.
        protected override object[] DescriptionArgs => new object[] { Damage, StunChance * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            f.AddOrGet<JuggernautLandingImpactUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->StunChance = StunChance;
            upgrade->StunDuration = config.StunDuration;
            upgrade->Source = this;
        }
    }
}
