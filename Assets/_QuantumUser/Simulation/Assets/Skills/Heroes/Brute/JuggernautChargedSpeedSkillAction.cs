namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, raises Juggernaut's Charged speed tier (not the baseline
    // Active tier - see JuggernautActiveSpeedSkillAction for that, a separate upgrade) by
    // SpeedBonusIncrease - see JuggernautChargedSpeedUpgrade and
    // JuggernautSkillData.ResolveChargedMoveSpeedBonus.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautChargedSpeedSkillAction : SkillActionData
    {
        public FP SpeedBonusIncrease = FP._0_25;

        public JuggernautChargedSpeedSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = SpeedBonusIncrease as a percent - e.g. "Increases Juggernaut's Charged move speed
        // bonus by {0}%."
        protected override object[] DescriptionArgs => new object[] { SpeedBonusIncrease * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautChargedSpeedUpgrade>(filter.Entity, out var upgrade);
            upgrade->SpeedBonusIncrease = SpeedBonusIncrease;
        }
    }
}
