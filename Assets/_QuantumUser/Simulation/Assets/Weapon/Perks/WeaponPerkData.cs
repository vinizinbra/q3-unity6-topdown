namespace Quantum
{
    using UnityEngine;

    // Bakes its effect straight into the Weapon's seeded stats at equip (see WeaponSystem.Equip)
    // instead of being re-derived every tick - a perk is never removed, so there's nothing to
    // recompute. Weapon.Perks still records which ones a roll contains, for UI.
    //
    // Icon/DisplayName come from UpgradeData; Rarity is its own field here (not shared - see
    // UpgradeData's own comment) since only WeaponPerkData/RiftMutationData still have a rarity
    // axis. Rarity decides what each rarity is actually worth via WeaponPerkPoolData.GetWeight
    // (drop rolls) or LevelUpConfig.GetWeight (level-up rolls). View-only Description lives in the
    // companion WeaponPerkData.View.cs partial.
    public abstract unsafe partial class WeaponPerkData : UpgradeData
    {
        [Tooltip("How likely this perk is to come up in a drop roll or a level-up roll - see WeaponPerkPoolData.GetWeight/LevelUpConfig.GetWeight.")]
        public UpgradeRarity Rarity = UpgradeRarity.Common;

        public abstract void Apply(Frame f, EntityRef owner, Weapon* weapon);

        public override string GetDescription() => GetFormattedDescription();
    }
}
