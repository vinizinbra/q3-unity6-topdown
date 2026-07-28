namespace Quantum
{
    // Hero Skill Upgrade - while equipped, every Interval-th damage pulse from the speaker also
    // stuns whoever it catches - see AlternatingAreaSystem.TryApplyStunUpgrade.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill produces" upgrade this session: re-granting fresh (idempotent) every activation and
    // never removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class StunEveryWavesSkillAction : SkillActionData
    {
        public byte Interval = 3;

        [ExpandableAsset] public AssetRef<HitEffectData> StunEffect;

        public StunEveryWavesSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Interval - e.g. "Every {0}rd damage pulse from the speaker also stuns whoever it
        // hits."
        protected override object[] DescriptionArgs => new object[] { Interval };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<StunEveryWavesUpgrade>(filter.Entity, out var upgrade);
            upgrade->Interval = Interval;
            upgrade->StunEffect = StunEffect;

            Log.Debug($"[Skill] {filter.Entity} granted StunEveryWavesUpgrade (every {Interval} waves, StunEffect valid: {StunEffect.IsValid})");
        }
    }
}
