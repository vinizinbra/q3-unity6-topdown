namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by StatUtility.GetProjectileSpeedMultiplier (ProjectileSpawner.Spawn). See
    // docs/global-upgrades.md.
    public unsafe class ProjectileSpeedUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->ProjectileSpeedMultiplier;
    }
}
