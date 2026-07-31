namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetSkillCooldown for SkillSlotId.HeroSkill only - see
    // DashCooldownUpgradeData for the Dash slot's independent multiplier. See
    // docs/global-upgrades.md.
    public unsafe class SkillCooldownUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->SkillCooldownMultiplier;
    }
}
