namespace Quantum
{
    // Hero Skill Upgrade - while equipped, a Juggernaut discharge no longer resets ChargePoints back
    // to 0, so Brutus stays Charged and knocks up every enemy he touches for the rest of the
    // activation instead of needing to re-charge between each one - see
    // JuggernautSustainedChargeUpgrade and JuggernautSkillData.TryDischarge.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures how
    // the skill behaves" upgrade this session: re-granting fresh (idempotent) every activation and
    // never removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautSustainedChargeSkillAction : SkillActionData
    {
        public JuggernautSustainedChargeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautSustainedChargeUpgrade>(filter.Entity, out _);
        }
    }
}
