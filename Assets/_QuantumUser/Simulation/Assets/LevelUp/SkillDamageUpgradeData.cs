namespace Quantum
{
    using Photon.Deterministic;

    // See docs/global-upgrades.md.
    public unsafe class SkillDamageUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->SkillDamageMultiplier;
    }
}
