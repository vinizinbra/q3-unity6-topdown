namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, raises Juggernaut's baseline Active speed tier (not the
    // Charged tier - see JuggernautChargedSpeedSkillAction for that, a separate upgrade) by
    // SpeedBonusIncrease - see JuggernautActiveSpeedUpgrade and
    // JuggernautSkillData.ResolveActiveMoveSpeedBonus.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautActiveSpeedSkillAction : SkillActionData
    {
        public FP SpeedBonusIncrease = FP._0_10;

        public JuggernautActiveSpeedSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautActiveSpeedUpgrade>(filter.Entity, out var upgrade);
            upgrade->SpeedBonusIncrease = SpeedBonusIncrease;
        }
    }
}
