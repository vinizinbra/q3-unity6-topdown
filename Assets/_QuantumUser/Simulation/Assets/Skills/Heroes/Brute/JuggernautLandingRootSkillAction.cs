namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every enemy Brutus launches with a discharge deals Damage
    // to itself and rolls RootChance for RuntimeConfig.EffectConfig.RootDuration the moment it
    // touches ground/a wall again - a parallel alternative to JuggernautLandingImpactSkillAction
    // (which stuns instead), see JuggernautLandingRootUpgrade, JuggernautLaunched and
    // JuggernautLandingImpactSystem. Root pins movement only (StatusEffectUtility.ApplyRoot) - the
    // target can still attack, unlike Stun. Only Duration is read from EffectConfig (same reasoning
    // as every other status effect) - Damage/RootChance stay authored here since they're this
    // specific skill's own tuning, not a property of Root itself.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautLandingRootSkillAction : SkillActionData
    {
        public FP Damage = 10;
        public FP RootChance = FP._0_50;

        public JuggernautLandingRootSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Damage, {1} = RootChance as a percent - e.g. "Deals {0} damage and roots enemies when
        // they land after being launched by a discharge, with a {1}% chance to trigger." Duration
        // comes from EffectConfig, not a local field, so it isn't templated here.
        protected override object[] DescriptionArgs => new object[] { Damage, RootChance * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            f.AddOrGet<JuggernautLandingRootUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->RootChance = RootChance;
            upgrade->RootDuration = config.RootDuration;
        }
    }
}
