namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetSkillDuration. See docs/global-upgrades.md.
    public unsafe class SkillDurationUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->SkillDurationMultiplier;
    }
}
