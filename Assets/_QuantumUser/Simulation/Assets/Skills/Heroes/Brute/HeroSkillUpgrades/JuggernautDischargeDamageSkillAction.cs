namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, every enemy actually launched by a Juggernaut discharge
    // also takes Damage, on top of the knockback - see JuggernautDischargeDamageUpgrade and
    // JuggernautSkillData.Discharge. Distinct from JuggernautEndExplosionSkillAction, which only
    // fires once when the channel expires, not on every individual discharge.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautDischargeDamageSkillAction : SkillActionData
    {
        public FP Damage = 10;

        public JuggernautDischargeDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Damage - e.g. "Deals {0} damage to every enemy hit by a Juggernaut discharge, in
        // addition to the knockback."
        protected override object[] DescriptionArgs => new object[] { Damage };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautDischargeDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = Damage;
        }
    }
}
