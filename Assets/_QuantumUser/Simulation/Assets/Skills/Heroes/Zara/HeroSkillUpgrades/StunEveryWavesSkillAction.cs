namespace Quantum
{
    // Hero Skill Upgrade - while equipped, every WaveInterval-th damage pulse from the speaker also
    // stuns whoever it catches - see AlternatingAreaSystem.TryApplyStunUpgrade.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill produces" upgrade this session: re-granting fresh (idempotent) every activation and
    // never removing it means it's simply always there once picked, with nothing to race against.
    //
    // Named WaveInterval rather than Interval to avoid colliding with the inherited
    // SkillActionData.Interval field (unrelated OnGoing-pacing concept) - Unity's serializer rejects
    // two fields with the same name across a MonoBehaviour/ScriptableObject inheritance chain.
    public unsafe partial class StunEveryWavesSkillAction : SkillActionData
    {
        public byte WaveInterval = 3;

        [ExpandableAsset] public AssetRef<HitEffectData> StunEffect;

        public StunEveryWavesSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = WaveInterval - e.g. "Every {0}rd damage pulse from the speaker also stuns whoever it
        // hits."
        protected override object[] DescriptionArgs => new object[] { WaveInterval };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<StunEveryWavesUpgrade>(filter.Entity, out var upgrade);
            upgrade->Interval = WaveInterval;
            upgrade->StunEffect = StunEffect;

            Log.Debug($"[Skill] {filter.Entity} granted StunEveryWavesUpgrade (every {WaveInterval} waves, StunEffect valid: {StunEffect.IsValid})");
        }
    }
}
