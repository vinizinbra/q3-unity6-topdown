namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetSkillCooldown for SkillSlotId.DashSkill only - see
    // SkillCooldownUpgradeData for the Hero Skill slot's independent multiplier. Expresses a rate
    // (higher = faster refresh), same divide-not-subtract convention as
    // StatUtility.GetFireCooldown/GetReloadDuration. See docs/global-upgrades.md.
    public unsafe class DashCooldownUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->DashCooldownMultiplier;
    }
}
