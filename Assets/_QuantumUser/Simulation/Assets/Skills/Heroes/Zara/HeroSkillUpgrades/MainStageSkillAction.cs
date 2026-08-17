namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Ascension (Main Stage, ranked, line 4/4 on Totem) - see docs/zara-ascensions.md.
    // Replaces Zara's own SpawnRadiusUpSkillAction instance ("Bigger Totem") with a dedicated
    // RadiusBonus field (see MainStage.qtn's own comment on why this is deliberately NOT the shared
    // SpawnRadiusUpgrade component), adds a Duration bonus (rank 2+), and (rank 3 "Main Stage") an
    // immediate opening Damage Beat on deploy plus a final Healing Beat on expiry - see
    // SpawnAlternatingAreaEffectData.Apply/AlternatingAreaSystem.FireBonusPulse/TryFireClosingBeat.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class MainStageSkillAction : SkillActionData
    {
        public FP[] RadiusBonus = { FP.FromString("0.30"), FP._0_50, FP.FromString("0.75") };
        public FP[] DurationBonus = { FP._0, FP._2, FP._2 };

        public MainStageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<MainStageUpgrade>(filter.Entity, out var upgrade);
            upgrade->RadiusBonus = RadiusBonus[index];
            upgrade->DurationBonus = DurationBonus[index];
            upgrade->Rank = (byte)rank;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
