namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every enemy Brutus launches with a discharge deals Damage
    // to itself and rolls RootChance for RootDuration the moment it touches ground/a wall again - a
    // parallel alternative to JuggernautLandingImpactSkillAction (which stuns instead), see
    // JuggernautLandingRootUpgrade, JuggernautLaunched and JuggernautLandingImpactSystem. Root pins
    // movement only (StatusEffectUtility.ApplyRoot) - the target can still attack, unlike Stun.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautLandingRootSkillAction : SkillActionData
    {
        public FP Damage = 10;
        public FP RootChance = FP._0_50;
        public FP RootDuration = 2;

        public JuggernautLandingRootSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautLandingRootUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->RootChance = RootChance;
            upgrade->RootDuration = RootDuration;
        }
    }
}
