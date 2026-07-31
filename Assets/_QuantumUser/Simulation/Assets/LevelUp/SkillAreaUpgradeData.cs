namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetAreaMultiplier (HitPathSkillAction/SpawnEntitySkillAction) -
    // stacks with SkillSlot.AreaMultiplier rather than replacing it, since that field resets to 1
    // every activation (see SkillSystem.TryBegin) and can't hold a permanent bonus itself. See
    // docs/global-upgrades.md.
    public unsafe class SkillAreaUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->AreaRadiusMultiplier;
    }
}
