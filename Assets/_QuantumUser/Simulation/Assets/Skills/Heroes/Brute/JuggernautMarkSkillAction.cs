namespace Quantum
{
    // Hero Skill Upgrade - while equipped, every enemy actually launched by a Juggernaut discharge
    // also gets MarkEffect applied (typically a MarkEffectData asset) - see JuggernautMarkUpgrade and
    // JuggernautSkillData.Discharge.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class JuggernautMarkSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> MarkEffect;

        public JuggernautMarkSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<JuggernautMarkUpgrade>(filter.Entity, out var upgrade);
            upgrade->MarkEffect = MarkEffect;
        }
    }
}
