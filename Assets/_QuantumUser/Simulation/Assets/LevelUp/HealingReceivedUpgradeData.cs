namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by HealUtility.ResolveHealMultiplier - applies to every heal (pickups, lifesteal,
    // HealthRegenUpgradeData's regen tick), not just one source. See docs/global-upgrades.md.
    public unsafe class HealingReceivedUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->HealingReceivedMultiplier;
    }
}
