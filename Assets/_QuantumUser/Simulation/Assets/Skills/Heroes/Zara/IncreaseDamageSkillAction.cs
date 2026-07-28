namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the speaker spawns with its damage wave DamageBonus
    // stronger than what's authored on SpawnAlternatingAreaEffectData.DamageAmount - see
    // SpawnAlternatingAreaEffectData.ResolveDamageAmount.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class IncreaseDamageSkillAction : SkillActionData
    {
        public FP DamageBonus = FP._0_50;

        public IncreaseDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = DamageBonus as a percent - e.g. "Increases the speaker's damage pulse by {0}% while
        // equipped."
        protected override object[] DescriptionArgs => new object[] { DamageBonus * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<IncreaseDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->DamageBonus = DamageBonus;
        }
    }
}
