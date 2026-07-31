namespace Quantum
{
    using Photon.Deterministic;

    // Stacks with Weapon.DamageMultiplier (Weapon Perks) rather than replacing it - two independent
    // sources of the same final damage scale, same "stack, don't replace" convention as
    // DamageUtility.GetSourceMultiplier. See docs/global-upgrades.md.
    public unsafe class WeaponDamageUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->WeaponDamageMultiplier;
    }
}
