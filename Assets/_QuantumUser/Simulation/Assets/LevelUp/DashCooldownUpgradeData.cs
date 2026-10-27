namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetSkillCooldown for SkillSlotId.DashSkill only - see
    // SkillCooldownUpgradeData for the Hero Skill slot's independent multiplier. Expresses a rate
    // (higher = faster refresh), same divide-not-subtract convention as
    // StatUtility.GetFireCooldown/GetReloadDuration; per-pick totals live in
    // CooldownReductionUpgradeData. See docs/global-upgrades.md.
    public unsafe class DashCooldownUpgradeData : CooldownReductionUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->DashCooldownMultiplier;
    }
}
