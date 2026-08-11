namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Juggernaut Ascension - absorbs the old baseline Momentum + Unstoppable concepts. Lives on
    // JuggernautSkillData.Actions (Activated = false), same "Hero Skill Ascension" shape Pixie's
    // ClusterBombSkillAction/BirthdayCakeSkillAction already use - not PassiveUpgradeData, since this
    // is thematically and mechanically a Juggernaut upgrade, not a hero-wide passive; it now correctly
    // shows as "Hero Skill" in the level-up UI/debug menu instead of a generic "Passive Upgrade".
    // Fires on every Juggernaut Begin, refreshing MomentumUpgrade's fields fresh off the live rank
    // each cast (same as Cluster Bomb re-setting its own upgrade component every throw) - functionally
    // identical to the old once-at-pick-time PassiveUpgradeData.Apply, since none of these values
    // depend on anything that changes activation to activation.
    public unsafe partial class MomentumSkillAction : SkillActionData
    {
        public FP[] GenerationMultiplier = { FP.FromString("1.25"), FP.FromString("1.40"), FP.FromString("1.40") };
        public FP[] ChargedMoveSpeedBonus = { FP._0_10, FP._0_20, FP.FromString("0.30") };
        public FP[] DischargeRetentionFraction = { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };

        public MomentumSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<MomentumUpgrade>(filter.Entity, out var upgrade);
            upgrade->GenerationMultiplier = GenerationMultiplier[index];
            upgrade->ChargedMoveSpeedBonus = ChargedMoveSpeedBonus[index];
            upgrade->DischargeRetentionFraction = DischargeRetentionFraction[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
