namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetSkillCooldown for SkillSlotId.HeroSkill only - see
    // DashCooldownUpgradeData for the Dash slot's independent multiplier, and
    // CooldownReductionUpgradeData for the exact per-pick totals. See docs/global-upgrades.md.
    public unsafe class SkillCooldownUpgradeData : CooldownReductionUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->SkillCooldownMultiplier;
    }
}
