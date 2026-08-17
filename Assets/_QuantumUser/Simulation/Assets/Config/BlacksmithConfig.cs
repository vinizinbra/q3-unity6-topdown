namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Per-Breathing-Break perk rarity weights - indexed by Clamp(Global.BreathingIndex, 0,
    // BreakTuning.Length - 1), same "last authored row holds forever past the authored range"
    // convention SurvivalConfig.Phases[] already uses. Reuses WeaponPerkPoolData purely as a POOL
    // (only its own Perks list is read) - these weights REPLACE that pool's own Common/Rare/Epic/
    // LegendaryWeight fields for a Blacksmith roll, since Blacksmith's whole point is to get
    // stronger/rarer as a run progresses, unlike a flat level-up weapon-perk pick.
    [Serializable]
    public struct BlacksmithBreakTuning
    {
        public int CommonWeight;
        public int RareWeight;
        public int EpicWeight;
        public int LegendaryWeight;

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

    // Tuning for the Blacksmith POI (see docs/store-blacksmith.md) - referenced from RuntimeConfig.
    // BlacksmithConfig, mirrors CursedRiftConfig's own "one config asset per POI" shape.
    public class BlacksmithConfig : AssetObject
    {
        public AssetRef<WeaponPerkPoolData> PerkPool;
        public int PerkChoiceCount = 3;

        // Confirmed with the user - Blacksmith perk picks cost Coins (same purchase UI/flow Store
        // uses, see docs/store-blacksmith.md), priced per the SPECIFIC perk's own Rarity rather
        // than one flat price for every offer - a Legendary perk should cost more than a Common
        // one. Resolved live off WeaponPerkData.Rarity (see ResolvePerkPrice), never baked into
        // BlacksmithInteraction - Rarity is a static asset field, nothing to snapshot.
        public FP CommonPerkPrice = 50;
        public FP RarePerkPrice = 100;
        public FP EpicPerkPrice = 175;
        public FP LegendaryPerkPrice = 300;

        public FP ResolvePerkPrice(UpgradeRarity rarity)
        {
            switch (rarity)
            {
                case UpgradeRarity.Common: return CommonPerkPrice;
                case UpgradeRarity.Rare: return RarePerkPrice;
                case UpgradeRarity.Epic: return EpicPerkPrice;
                case UpgradeRarity.Legendary: return LegendaryPerkPrice;
                default: return CommonPerkPrice;
            }
        }

        public BlacksmithBreakTuning[] BreakTuning =
        {
            new BlacksmithBreakTuning { CommonWeight = 85, RareWeight = 15, EpicWeight = 0, LegendaryWeight = 0 },
            new BlacksmithBreakTuning { CommonWeight = 70, RareWeight = 28, EpicWeight = 2, LegendaryWeight = 0 },
            new BlacksmithBreakTuning { CommonWeight = 50, RareWeight = 45, EpicWeight = 5, LegendaryWeight = 0 },
            new BlacksmithBreakTuning { CommonWeight = 30, RareWeight = 60, EpicWeight = 10, LegendaryWeight = 0 },
        };

        public BlacksmithBreakTuning ResolveBreakTuning(int breathingIndex)
        {
            if (BreakTuning == null || BreakTuning.Length == 0)
                return default;

            int index = breathingIndex < 0 ? 0 : breathingIndex;
            index = index < BreakTuning.Length ? index : BreakTuning.Length - 1;

            return BreakTuning[index];
        }
    }
}
