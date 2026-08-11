namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Juggernaut Ascension - "Discharge damage progression." Lives on JuggernautSkillData.
    // Actions (Activated = false) - see MomentumSkillAction's own comment for why this is a Hero Skill
    // Ascension, not PassiveUpgradeData. Discharge deals JuggernautSkillData.Damage unconditionally
    // (a real baseline number, not gated behind this or any Ascension) - this scales that baseline up,
    // plus (rank 3) an additional bonus specifically against Specialist/Heavy tier targets. See
    // JuggernautSkillData.Discharge - reads this component directly and feeds the already-scaled value
    // into the existing DamageUtility.ApplyDamage entry point, so it still flows through the normal
    // outgoing-damage pipeline (crit, global multipliers) rather than duplicating it.
    public unsafe partial class BoneBreakerSkillAction : SkillActionData
    {
        public FP[] DamageMultiplierBonus = { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
        public FP[] TierDamageBonus = { FP._0, FP._0, FP.FromString("0.30") };

        public BoneBreakerSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<BoneBreakerUpgrade>(filter.Entity, out var upgrade);
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->TierDamageBonus = TierDamageBonus[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
