namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, a Juggernaut discharge pushes and pops enemies harder -
    // ForceBonus scales both KnockbackForce and KnockbackUpwardForce together, not just the radius -
    // see JuggernautKnockbackUpgrade and JuggernautSkillData.ResolveKnockbackMultiplier.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautKnockbackSkillAction : SkillActionData
    {
        public FP ForceBonus = FP._0_50;

        public JuggernautKnockbackSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautKnockbackUpgrade>(filter.Entity, out var upgrade);
            upgrade->ForceBonus = ForceBonus;
        }
    }
}
