namespace Quantum
{
    // Bakes its effect straight into the Weapon's seeded stats at equip (see WeaponSystem.Equip)
    // instead of being re-derived every tick - a perk is never removed, so there's nothing to
    // recompute. Weapon.Perks still records which ones a roll contains, for UI.
    //
    // View-only fields live in the companion WeaponPerkData.View.cs partial.
    public abstract unsafe partial class WeaponPerkData : AssetObject
    {
        // How likely this perk is to come up in a random roll - see WeaponPerkPoolData, which
        // decides what each rarity is actually worth.
        public WeaponPerkRarity Rarity = WeaponPerkRarity.Common;

        public abstract void Apply(Frame f, Weapon* weapon);
    }
}
