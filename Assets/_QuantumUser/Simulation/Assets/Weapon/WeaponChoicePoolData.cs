namespace Quantum
{
    using System.Collections.Generic;

    // Pool LevelUpUtility.RollChooseWeaponOptionsFor draws 3 distinct weapons from for a
    // LevelUpCategory.ChooseWeapon pick - referenced from LevelUpConfig.WeaponChoicePool. No weight
    // table (unlike WeaponPerkPoolData) - a WeaponDataAsset carries no Rarity of its own, so every
    // listed weapon is equally likely to be drawn.
    public class WeaponChoicePoolData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<WeaponDataAsset>> Weapons = new();
    }
}
