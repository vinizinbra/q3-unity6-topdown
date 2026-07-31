namespace Quantum
{
    using Photon.Deterministic;

    // Stacks with Weapon.CriticalDamageBonus (Weapon Perks) rather than replacing it - see
    // WeaponDamageUpgradeData. See docs/global-upgrades.md.
    public unsafe class CriticalDamageUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->CriticalDamageMultiplier;
    }
}
