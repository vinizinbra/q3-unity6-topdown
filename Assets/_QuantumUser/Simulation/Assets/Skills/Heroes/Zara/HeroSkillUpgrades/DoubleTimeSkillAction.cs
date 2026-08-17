namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Ascension (Double Time, ranked, line 3/4 on Totem) - see docs/zara-ascensions.md.
    // Replaces IncreaseWavesTickRateSkillAction with a direct per-rank interval override rather than
    // a rate multiplier - the spec pins exact seconds (1.0 -> 0.85 -> 0.70 -> 0.50), not percentages.
    // Baked once at Totem/Speaker spawn - see SpawnAlternatingAreaEffectData.ResolveTickInterval.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class DoubleTimeSkillAction : SkillActionData
    {
        public FP[] BeatInterval = { FP.FromString("0.85"), FP.FromString("0.70"), FP._0_50 };

        public DoubleTimeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<DoubleTimeUpgrade>(filter.Entity, out var upgrade);
            upgrade->BeatInterval = BeatInterval[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
