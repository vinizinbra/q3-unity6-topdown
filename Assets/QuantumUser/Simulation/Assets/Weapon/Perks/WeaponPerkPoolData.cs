namespace Quantum
{
    using System.Collections.Generic;

    // Pool a random weapon roll draws from - see WeaponGenerator. Each perk carries its own Rarity;
    // this decides what each rarity is worth relative to the others.
    public class WeaponPerkPoolData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<WeaponPerkData>> Perks = new();

        public int CommonWeight = 100;
        public int UncommonWeight = 50;
        public int RareWeight = 20;
        public int EpicWeight = 5;
        public int LegendaryWeight = 1;

        // 0 or less benches a rarity outright - it can never be drawn, without deleting anything
        // from Perks.
        public int GetWeight(WeaponPerkRarity rarity)
        {
            switch (rarity)
            {
                case WeaponPerkRarity.Common: return CommonWeight;
                case WeaponPerkRarity.Uncommon: return UncommonWeight;
                case WeaponPerkRarity.Rare: return RareWeight;
                case WeaponPerkRarity.Epic: return EpicWeight;
                case WeaponPerkRarity.Legendary: return LegendaryWeight;
                default: return 0;
            }
        }
    }
}
