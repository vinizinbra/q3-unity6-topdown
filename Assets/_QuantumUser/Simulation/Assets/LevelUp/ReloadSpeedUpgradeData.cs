namespace Quantum
{
    using Photon.Deterministic;

    // Stacks with Weapon.ReloadDuration (Weapon Perks) rather than replacing it - see
    // WeaponDamageUpgradeData. See docs/global-upgrades.md.
    public unsafe class ReloadSpeedUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->ReloadSpeedMultiplier;
    }
}
