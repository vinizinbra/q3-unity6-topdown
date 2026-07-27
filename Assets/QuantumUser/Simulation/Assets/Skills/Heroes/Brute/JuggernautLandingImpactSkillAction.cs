namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every enemy Brutus launches with a discharge deals Damage
    // to itself and rolls StunChance for StunDuration the moment it touches ground again - see
    // JuggernautLandingImpactUpgrade, JuggernautLaunched and JuggernautLandingImpactSystem. Distinct
    // from JuggernautDischargeDamageSkillAction (damage on launch) and JuggernautEndExplosionSkillAction
    // (damage on channel expiry) - this one fires on landing specifically.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautLandingImpactSkillAction : SkillActionData
    {
        public FP Damage = 10;
        public FP StunChance = FP._0_50;
        public FP StunDuration = 2;

        public JuggernautLandingImpactSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautLandingImpactUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
            upgrade->StunChance = StunChance;
            upgrade->StunDuration = StunDuration;
            upgrade->Source = this;
        }
    }
}
