namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Juggernaut Ascension - absorbs the old Aftershock + Building Pressure concepts. Lives on
    // JuggernautSkillData.Actions (Activated = false) - see MomentumSkillAction's own comment for why
    // this is a Hero Skill Ascension, not PassiveUpgradeData. "Building Pressure" is internal state,
    // not a standalone mechanic - it reuses the already-existing JuggernautCharge.UnitsHit (cumulative
    // enemies knocked back by Discharge this whole activation) as its stack count directly, no new
    // component needed; resets for free every activation since JuggernautCharge itself is removed at
    // End regardless. See JuggernautSkillData.TryEndExplosion. Bakes Source as a self-reference every
    // Begin so the view can resolve BlastEffectPrefab off the exact asset that granted this - same
    // pattern GroundPoundPassiveUpgradeData.Source/VortexExplodeOnDestroy.Source already use.
    public unsafe partial class AftershockSkillAction : SkillActionData
    {
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.20"), FP.FromString("1.20") };
        public FP[] StackDamagePercent = { FP._0, FP.FromString("0.15"), FP.FromString("0.20") };
        public byte[] MaxStacks = { 0, 6, 8 };

        public AftershockSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<AftershockUpgrade>(filter.Entity, out var upgrade);
            upgrade->RadiusMultiplier = RadiusMultiplier[index];
            upgrade->StackDamagePercent = StackDamagePercent[index];
            upgrade->MaxStacks = MaxStacks[index];
            upgrade->StunsAtHighPressure = rank >= 3;
            upgrade->Source = this;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
