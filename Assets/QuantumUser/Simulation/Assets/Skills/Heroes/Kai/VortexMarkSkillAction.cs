namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the vortex applies MarkEffect (typically a MarkEffectData
    // asset - "Void Mark") to every enemy it pulls, refreshed each pull pulse - see VortexMarkUpgrade
    // and VortexSystem.TryApplyMark.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexMarkSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> MarkEffect;

        public VortexMarkSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexMarkUpgrade>(filter.Entity, out var upgrade);
            upgrade->MarkEffect = MarkEffect;
        }
    }
}
