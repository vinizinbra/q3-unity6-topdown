namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Every number a meta-progression level is worth (docs/talents.md, "Meta progression"). The menu
    // only ever ships LEVELS (RuntimePlayer.Talents / RuntimeConfig.WorldUnlocks); arrays below are
    // indexed by that level (index 0 = not bought) and clamped to their last entry - see TalentUtility.
    // Perk rarity weights for a newly rolled weapon, by the Weapons meta line (TalentsConfig.
    // WeaponPerkRarityByLevel) - same shape as BlacksmithConfig.BlacksmithBreakTuning (Common/Rare/Epic/
    // Legendary weights + GetWeight), mirrored rather than shared: that one is indexed by Breathing Break.
    [System.Serializable]
    public struct WeaponTalentRarityTuning
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

    public class TalentsConfig : AssetObject
    {
        // One shared step for every leveling Power talent (PlayerDamageLevel, PlayerCooldownLevel,
        // etc.) - not per-stat, not per-level-index. Level N bonus = N * PercentPerLevel. See
        // TalentUtility.ApplyPerPlayerTalents.
        public FP PercentPerLevel = 5;

        // Coin Gain is the one Power line on a bigger step (its menu card reads +10%/level).
        public FP CoinGainPercentPerLevel = 10;

        [Header("Economy")]
        // Starting Gold line -> extra Coins in this player's own wallet at spawn.
        public int[] StartingGoldByLevel = { 0, 100, 200, 300, 400, 500 };

        // Rerolls line -> extra level-up/Chest rerolls at spawn.
        public int[] RerollsByLevel = { 0, 1, 2 };

        [Header("Revive")]
        // Revive line -> self-revive charges (L1 grants one, L4 "Extra Revive" a second).
        public int[] SelfReviveChargesByLevel = { 0, 1, 1, 1, 2 };

        // Revive L2 "Revive Recovery" - extra fraction of Max Health on top of ReviveConfig.
        // ReviveHealthPercent when a player revives THEMSELVES (self-revive charge).
        public FP SelfReviveHealthBonus = FP._0_20;

        [Header("Co-op")]
        // Co-op L1 "Fast Rescue" - this player's revive-hold progress runs this much faster.
        public FP CoopRescueSpeedBonus = FP._0_20;

        // Co-op L2 "Last Stand" - this player's own Downed bleed-out lasts this much longer.
        public FP CoopBleedOutBonus = FP._0_20;

        // Co-op L3 "Rescue Recovery" - an ally THIS player revives gets this extra fraction of Max Health.
        public FP CoopRescueHealthBonus = FP._0_20;

        [Header("Breathing POIs")]
        // Healing Shrine L2 "Improved Healing" - added to the shrine's own HealPercent.
        public FP ShrineImprovedHealBonus = FP._0_10;

        // Healing Shrine L3 "Faster Recovery" - multiplies the shrine's own CooldownDuration.
        public FP ShrineCooldownMultiplier = FP._0_75;

        [Header("Weapons")]
        // Weapons line (RuntimePlayer.Talents.WeaponLevel) - extra levels on EVERY weapon the player equips
        // this run (starting weapon included), on top of the offer's own SurvivalTime level.
        public int[] WeaponLevelBonusByLevel = { 0, 1, 2, 3, 4, 5 };

        // Weapons line - perk rarity weights for EVERY newly rolled weapon (Choose Weapon level-up, chest and
        // Store alike: same minute + same Weapons level = same roll). Index = Weapons level; index 0 is
        // unused - level 0 keeps the perk pool's own weights (WeaponPerkPoolData Common/Rare/Epic/Legendary).
        public WeaponTalentRarityTuning[] WeaponPerkRarityByLevel =
        {
            default,
            new WeaponTalentRarityTuning { CommonWeight = 100, RareWeight = 28, EpicWeight = 7, LegendaryWeight = 1 },
            new WeaponTalentRarityTuning { CommonWeight = 100, RareWeight = 36, EpicWeight = 10, LegendaryWeight = 2 },
            new WeaponTalentRarityTuning { CommonWeight = 100, RareWeight = 45, EpicWeight = 13, LegendaryWeight = 3 },
            new WeaponTalentRarityTuning { CommonWeight = 100, RareWeight = 55, EpicWeight = 17, LegendaryWeight = 4 },
            new WeaponTalentRarityTuning { CommonWeight = 100, RareWeight = 65, EpicWeight = 22, LegendaryWeight = 6 },
        };

        // Null = use the perk pool's own weights (level 0, or no row authored).
        public WeaponTalentRarityTuning? ResolveWeaponPerkRarity(int weaponsLevel)
        {
            if (weaponsLevel <= 0 || WeaponPerkRarityByLevel == null || WeaponPerkRarityByLevel.Length <= 1)
                return null;
            return WeaponPerkRarityByLevel[weaponsLevel < WeaponPerkRarityByLevel.Length ? weaponsLevel : WeaponPerkRarityByLevel.Length - 1];
        }

        [Header("Base (Rift Anchor)")]
        // Rift Anchor L3 - the Base's starting Weapon Chest rolls every weapon option with at least this
        // many perks (Chest.MinWeaponPerks, set by TalentGateSystem).
        public int BaseChestGuaranteedPerks = 1;

        [Header("Gunsmith")]
        // Gunsmith L3 "Master Craftsman" - rarity rows the Blacksmith reads ahead in
        // BlacksmithConfig.BreakTuning (as if this many Breaks later).
        public int GunsmithRarityBreakOffset = 2;

        public static int Resolve(int[] table, int level)
        {
            if (table == null || table.Length == 0 || level <= 0)
                return 0;
            return table[level < table.Length ? level : table.Length - 1];
        }
    }
}
