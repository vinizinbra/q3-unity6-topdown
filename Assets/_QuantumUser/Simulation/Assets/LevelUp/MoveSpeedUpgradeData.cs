namespace Quantum
{
    using Photon.Deterministic;

    // See docs/global-upgrades.md.
    public unsafe class MoveSpeedUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->MoveSpeedMultiplier;
    }
}
