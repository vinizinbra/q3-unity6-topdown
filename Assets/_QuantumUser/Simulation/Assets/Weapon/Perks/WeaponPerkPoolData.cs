namespace Quantum
{
    using System.Collections.Generic;

    // Pool a random weapon roll draws from - see WeaponGenerator. Each perk carries its own Rarity;
    // this decides what each rarity is worth relative to the others.
    public class WeaponPerkPoolData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<WeaponPerkData>> Perks = new();

        public int CommonWeight = 100;
        public int RareWeight = 20;
        public int EpicWeight = 5;
        public int LegendaryWeight = 1;

        // 0 or less benches a rarity outright - it can never be drawn, without deleting anything
        // from Perks.
        public int GetWeight(UpgradeRarity rarity)
        {
            switch (rarity)
            {
                case UpgradeRarity.Common: return CommonWeight;
                case UpgradeRarity.Rare: return RareWeight;
                case UpgradeRarity.Epic: return EpicWeight;
                case UpgradeRarity.Legendary: return LegendaryWeight;
                default: return 0;
            }
        }
    }
}
