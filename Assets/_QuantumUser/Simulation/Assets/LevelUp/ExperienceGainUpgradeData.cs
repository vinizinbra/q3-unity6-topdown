namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by CurrencyOrbSystem right where it credits a pickup to the shared run-wide total -
    // scales by whichever player actually walked into the orb's radius, same as
    // CharacterStats.PickupRangeMultiplier just above it. See docs/global-upgrades.md.
    public unsafe class ExperienceGainUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->ExperienceGainMultiplier;
    }
}
