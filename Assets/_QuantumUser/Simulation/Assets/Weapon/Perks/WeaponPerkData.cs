namespace Quantum
{
    // Bakes its effect straight into the Weapon's seeded stats at equip (see WeaponSystem.Equip)
    // instead of being re-derived every tick - a perk is never removed, so there's nothing to
    // recompute. Weapon.Perks still records which ones a roll contains, for UI.
    //
    // Icon/DisplayName/Rarity come from UpgradeData - Rarity decides what each rarity is actually
    // worth via WeaponPerkPoolData.GetWeight (drop rolls) or LevelUpConfig.GetWeight (level-up
    // rolls). View-only Description lives in the companion WeaponPerkData.View.cs partial.
    public abstract unsafe partial class WeaponPerkData : UpgradeData
    {
        public abstract void Apply(Frame f, Weapon* weapon);

        public override string GetDescription() => GetFormattedDescription();
    }
}
