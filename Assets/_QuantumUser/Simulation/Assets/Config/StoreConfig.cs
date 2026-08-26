namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    // Break-progression tuning for a freshly-rolled Store weapon offer (see
    // StoreConfig.BreakWeaponConfig/StoreUtility.RollWeaponOffers) - indexed by
    // Clamp(Global.BreathingIndex, 0, BreakWeaponConfig.Length - 1), same "last authored row holds
    // forever past the authored range" convention BlacksmithConfig.BreakTuning/SurvivalConfig.
    // Phases[] already use. Controls ONLY Weapon Level and starting perk COUNT - deliberately
    // independent of ShopWeaponOfferCount (offer CHOICE count, see StoreUtility.
    // ResolveWeaponOfferCount) and TalentRarityTuning below (perk RARITY) - see
    // docs/store-blacksmith.md's "Break Progression" section.
    [Serializable]
    public struct StoreBreakWeaponConfig
    {
        // Applied via WeaponSystem.AddLevel (reused unchanged, see StoreUtility.
        // ApplyBreakWeaponLevel) AFTER WeaponChoiceUtility.Grant, since Grant/Equip always resets a
        // freshly-equipped Weapon to Level 0.
        public byte WeaponLevel;

        // Each entry is an INDEPENDENT Bernoulli chance rolled in order (see StoreUtility.
        // RollStorePerkCount) - the number of successes is this weapon's starting perk count.
        // Deliberately NOT the "clamp01((weaponTalentLevel - slot) * ChancePerLevelPerSlot)" formula
        // LevelUpUtility.RollWeaponOption uses for a Choose-Weapon pick - Store's perk count is
        // Break-driven, not Weapon-Talent-driven, so it needs its own explicit, independently
        // configurable array per Break rather than a derived formula.
        public FP[] StartingPerkRolls;
    }

    // Weapon-Talent-Level-driven perk rarity tuning for a freshly-rolled Store weapon offer (see
    // StoreConfig.TalentRarityTuning/StoreUtility.RollStorePerks) - same shape as
    // BlacksmithConfig.BlacksmithBreakTuning (Common/Rare/Epic/Legendary weights + GetWeight),
    // deliberately mirrored rather than shared: that one is indexed by Breathing Break, this one by
    // RuntimePlayer.Talents.WeaponLevel (the SAME account-level stat StoreUtility.
    // ResolveWeaponLevelTalent already reads for weapon-offer selection - see its own comment on why
    // that's deliberately not the live in-run CharacterStats.WeaponTalentLevel).
    [Serializable]
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

    // Tuning for the Store POI (see docs/store-blacksmith.md) - referenced from RuntimeConfig.
    // StoreConfig, mirrors CursedRiftConfig's own "one config asset per POI" shape.
    public class StoreConfig : AssetObject
    {
        // Reuses the exact same pool type LevelUpConfig.WeaponChoicePool already draws a
        // Choose-Weapon level-up's 3 rolled weapons from - Store's own weapon offers are rolled the
        // same way (see StoreUtility.RollWeaponOffers), just with a different, per-player-talent-
        // driven offer count. Point this at the SAME asset as LevelUpConfig.WeaponChoicePool by
        // default, or a curated Store-only pool if a designer wants the two to diverge later.
        public AssetRef<WeaponChoicePoolData> WeaponPool;

        public int MaxWeaponOfferSlots = 3;

        public FP WeaponOfferBasePrice = 100;
        public FP WeaponOfferPricePerPerk = 25; // Price = Base + RolledPerkCount * PricePerPerk

        [Header("Break Progression (Weapon Level / Starting Perk Count)")]
        public StoreBreakWeaponConfig[] BreakWeaponConfig =
        {
            new StoreBreakWeaponConfig { WeaponLevel = 0, StartingPerkRolls = new[] { FP._0_20 } },
            new StoreBreakWeaponConfig { WeaponLevel = 1, StartingPerkRolls = new[] { FP.FromString("0.45"), FP._0_20 } },
            new StoreBreakWeaponConfig { WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.65"), FP.FromString("0.40"), FP._0_20 } },
            new StoreBreakWeaponConfig { WeaponLevel = 3, StartingPerkRolls = new[] { FP.FromString("0.80"), FP.FromString("0.60"), FP.FromString("0.40"), FP._0_20 } },
        };

        public StoreBreakWeaponConfig ResolveBreakWeaponConfig(int breathingIndex)
        {
            if (BreakWeaponConfig == null || BreakWeaponConfig.Length == 0)
                return default;

            int index = breathingIndex < 0 ? 0 : breathingIndex;
            index = index < BreakWeaponConfig.Length ? index : BreakWeaponConfig.Length - 1;

            return BreakWeaponConfig[index];
        }

        [Header("Weapon Talent Level -> Starting Perk Rarity")]
        public WeaponTalentRarityTuning[] TalentRarityTuning =
        {
            new WeaponTalentRarityTuning { CommonWeight = 90, RareWeight = 10, EpicWeight = 0, LegendaryWeight = 0 },
            new WeaponTalentRarityTuning { CommonWeight = 75, RareWeight = 25, EpicWeight = 0, LegendaryWeight = 0 },
            new WeaponTalentRarityTuning { CommonWeight = 55, RareWeight = 35, EpicWeight = 10, LegendaryWeight = 0 },
            new WeaponTalentRarityTuning { CommonWeight = 35, RareWeight = 45, EpicWeight = 18, LegendaryWeight = 2 },
        };

        public WeaponTalentRarityTuning ResolveTalentRarityTuning(int weaponTalentLevel)
        {
            if (TalentRarityTuning == null || TalentRarityTuning.Length == 0)
                return default;

            int index = weaponTalentLevel < 0 ? 0 : weaponTalentLevel;
            index = index < TalentRarityTuning.Length ? index : TalentRarityTuning.Length - 1;

            return TalentRarityTuning[index];
        }

        public AssetRef<FoodOfferPoolData> FoodPool;
        public int FoodOfferCount = 2;

        // The two GUARANTEED, never-rolled offers that share the food/utility row with the rolled
        // FoodOffers above. Both are toggles rather than fixed slots so a designer owns the row's
        // contents: whatever is enabled is packed in order after the rolled offers, and the row
        // needs FoodOfferCount + (however many of these are on) card slots on ChooseWindow.cardCount.
        //
        // Defaults give FoodOfferCount 2 + accessory service = exactly 3, matching the stock
        // cardCount of 3. Increase Weapon Level ships OFF for that reason - it is not deleted, just
        // not competing for a slot; turning it back on requires raising cardCount to 4.
        [Header("Guaranteed Offers")]
        [Tooltip("Show the \"Increase Weapon Level\" offer (see BuyWeaponLevelUp). OFF by default so the row fits in 3 card slots - turning it on needs ChooseWindow.cardCount raised to 4.")]
        public bool OfferWeaponLevelUp = false;

        [Tooltip("Show the Accessory Repair/Replacement service (see AccessoryServiceUtility/docs/accessory-guard.md). The card is only actually populated when the buyer's accessory is damaged or broken - at full durability the slot is reserved but empty, deliberately, so the row never reflows as durability changes.")]
        public bool OfferAccessoryService = true;

        // "Increase Weapon Level" - a guaranteed offer, always present every Breathing Break
        // (unlike WeaponOffers/FoodOffers, nothing rolled/random about it - see
        // StoreUtility.BuyWeaponLevelUp). Levels up the buyer's own currently-equipped Weapon
        // (Weapon.Level -> WeaponSystem.AddLevel), NOT CharacterStats.WeaponTalentLevel or the
        // meta-progression Talent - see Weapon.qtn's own comment on why those three are kept
        // separate. Price scales with the weapon's CURRENT Level so repeat purchases get pricier,
        // same "Base + PerUnit * count" shape WeaponOfferBasePrice/WeaponOfferPricePerPerk already
        // use above.
        public FP WeaponLevelUpBasePrice = 100;
        public FP WeaponLevelUpPricePerLevel = 50;
        public FP WeaponLevelUpDamageBonusPerLevel = FP._0_05; // +5% damage per level, compounding
    }
}
