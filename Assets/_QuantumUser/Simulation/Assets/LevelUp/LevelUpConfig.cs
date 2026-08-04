namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;

    // Global tuning for the level-up upgrade-choice screen - see docs/level-up-upgrades.md,
    // LevelUpUtility and LevelUpSystem. Referenced via RuntimeConfig.LevelUpConfig.
    //
    // Only WeaponPerk and GlobalUpgrade are pooled globally here - SkillUpgrade and PassiveUpgrade
    // are per-hero instead (see CharacterData.DashSkillUpgrades/PassiveUpgrades and HeroSkill's own
    // Actions - LevelUpUtility.AddHeroSkillUpgradeCandidates), since which skill/passive upgrades
    // make sense depends on which hero is picking. Every
    // candidate across all four kinds is weighted the same way, by its own UpgradeData.Rarity via
    // GetWeight below - independent of WeaponPerkPoolData's own (differently-tuned) weights, which
    // stay reserved for the original drop-roll mechanic (see WeaponGenerator).
    //
    // LevelSequence/WeaponChoicePool/ChancePerLevelPerSlot/MaxRolledPerks below configure the newer
    // per-level category locking (LevelUpCategory) and the ChooseWeapon category specifically - see
    // LevelUpUtility.GetCategoryForLevel/RollChooseWeaponOptionsFor and Chest.qtn (a Chest reuses
    // this same config, forced to its own single category).
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

        // Pooled globally same as GlobalUpgrades above, but a separate list/rarity axis - Rift
        // Mutations are non-stackable (see RiftMutationData/RiftMutationPicks), so they need their
        // own pick-history component rather than reusing GlobalUpgradePicks. See
        // docs/rift-mutations.md.
        [ExpandableAsset] public List<AssetRef<RiftMutationData>> RiftMutations = new();

        [Header("Category sequence")]
        // Which single LevelUpCategory a given level is locked to - LevelUpUtility.
        // GetCategoryForLevel indexes this cyclically as sequence[(level - 1) % sequence.Count]
        // (level is 1-based - the level a player is currently choosing an upgrade FOR). Empty
        // (default) means "no sequence configured" - RollOptionsFor falls back to the original
        // mixed-all-categories roll for every level, so an unedited LevelUpConfig.asset keeps
        // behaving exactly as it does today. See docs/level-up-upgrades.md.
        public List<LevelUpCategory> LevelSequence = new();

        [Header("Choose Weapon")]
        // Pool LevelUpUtility.RollChooseWeaponOptionsFor draws 3 distinct weapons from. Ships
        // unassigned until Editor-authored.
        public AssetRef<WeaponChoicePoolData> WeaponChoicePool;

        // Perk-count roll for a Choose-Weapon pick: slot i (0-based, up to MaxRolledPerks)
        // independently succeeds with probability clamp01((WeaponTalentLevel - i) *
        // ChancePerLevelPerSlot); the number of successes is that weapon's rolled perk count. E.g.
        // at the defaults below, WeaponTalentLevel 1 -> slot0 20% (matches "level 1 = 20% chance of
        // 1 perk"), slot1 0%; WeaponTalentLevel 2 -> slot0 40%, slot1 20% (matches "level 2 = 40%
        // chance of 1st perk, 20% chance of 2nd perk"). See CharacterStats.WeaponTalentLevel.
        public FP ChancePerLevelPerSlot = FP.FromString("0.2");
        public int MaxRolledPerks = 3;

        [Header("Rarity weights")]
        public int CommonWeight = 100;
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
                case UpgradeRarity.Rare: return RareWeight;
                case UpgradeRarity.Epic: return EpicWeight;
                case UpgradeRarity.Legendary: return LegendaryWeight;
                default: return 0;
            }
        }
    }
}
