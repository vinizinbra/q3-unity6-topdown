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

        // Whether this perk can express itself at all on a weapon of the given fire type - checked
        // by every draw site (WeaponGenerator/LevelUpUtility/StoreUtility/BlacksmithUtility) so a
        // perk that would do nothing is never offered rather than being a wasted pick. Almost every
        // perk either bakes into Weapon's own stats or reacts to a hit and works on both fire types,
        // so the default is true; SplitShotWeaponPerkData is the one that overrides it - see there
        // for why it, unlike Piercing Rounds/Ricochet/Critical Rebound, has no hitscan reading.
        public virtual bool SupportsFireType(WeaponFireType fireType) => true;

        public override string GetDescription() => GetFormattedDescription();
    }
}
