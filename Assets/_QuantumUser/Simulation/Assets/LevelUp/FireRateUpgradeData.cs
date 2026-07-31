namespace Quantum
{
    using Photon.Deterministic;

    // Stacks with Weapon.FireCooldownMultiplier (Weapon Perks) rather than replacing it - see
    // WeaponDamageUpgradeData. See docs/global-upgrades.md.
    public unsafe class FireRateUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->AttackSpeedMultiplier;
    }
}
