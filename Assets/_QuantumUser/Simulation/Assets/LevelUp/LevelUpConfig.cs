namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;

    // Global tuning for the level-up upgrade-choice screen - see docs/level-up-upgrades.md,
    // LevelUpUtility and LevelUpSystem. Referenced via RuntimeConfig.LevelUpConfig.
    //
    // Only WeaponPerk and GlobalUpgrade are pooled globally here - SkillUpgrade and PassiveUpgrade
    // are per-hero instead (see CharacterData.DashSkillUpgrades/HeroSkillUpgrades/PassiveUpgrades),
    // since which skill/passive upgrades make sense depends on which hero is picking. Every
    // candidate across all four kinds is weighted the same way, by its own UpgradeData.Rarity via
    // GetWeight below - independent of WeaponPerkPoolData's own (differently-tuned) weights, which
    // stay reserved for the original drop-roll mechanic (see WeaponGenerator).
    public class LevelUpConfig : AssetObject
    {
        // How long players get to pick before LevelUpUtility.Resolve auto-picks for anyone
        // unconfirmed - see LevelUpSystem.Update.
        public FP DecisionTimeSeconds = 30;

        // How many options LevelUpUtility.RollOptionsFor tries to roll per player - the combined
        // pool may hold fewer, in which case LevelUpChoice.OptionCount ends up lower than this.
        public int ChoiceCount = 3;

        // Reuses WeaponPerkData's own existing pool type - see WeaponPerkPoolData/WeaponGenerator.
        // Only WeaponPerkPoolData.Perks (which perks are eligible) is read for level-up rolling;
        // its own weight fields are not - see GetWeight below.
        public AssetRef<WeaponPerkPoolData> WeaponPerkPool;

        // Plumbing only for now - ships empty until Global Upgrades are actually designed. See
        // GlobalUpgradeData/GlobalUpgradeUtility.
        [ExpandableAsset] public List<AssetRef<GlobalUpgradeData>> GlobalUpgrades = new();

        [Header("Rarity weights")]
        public int CommonWeight = 100;
        public int UncommonWeight = 50;
        public int RareWeight = 20;
        public int EpicWeight = 5;
        public int LegendaryWeight = 1;

        // 0 or less benches a rarity outright - it can never be drawn, without deleting anything
        // from any pool. Same shape as WeaponPerkPoolData.GetWeight, kept separate deliberately -
        // level-up pacing may want different tuning than raw drop rolls.
        public int GetWeight(UpgradeRarity rarity)
        {
            switch (rarity)
            {
                case UpgradeRarity.Common: return CommonWeight;
                case UpgradeRarity.Uncommon: return UncommonWeight;
                case UpgradeRarity.Rare: return RareWeight;
                case UpgradeRarity.Epic: return EpicWeight;
                case UpgradeRarity.Legendary: return LegendaryWeight;
                default: return 0;
            }
        }
    }
}
