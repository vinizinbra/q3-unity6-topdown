namespace Quantum
{
    // Shared rarity tier for anything offerable as a level-up upgrade card - see UpgradeData,
    // LevelUpConfig.GetWeight. Used to be WeaponPerkData-only (WeaponPerkRarity); generalized here
    // once SkillActionData/GlobalUpgradeData/PassiveUpgradeData all needed the same concept.
    public enum UpgradeRarity : byte
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }
}
