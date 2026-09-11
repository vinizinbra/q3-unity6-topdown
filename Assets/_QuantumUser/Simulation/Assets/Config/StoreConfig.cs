namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

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

        // Weapon Level / starting perk COUNT for a freshly-rolled Store weapon offer are no longer
        // authored here - both now come from the shared LevelUpConfig.WeaponOfferCurve (keyed by
        // Global.SurvivalTime instead of Global.BreathingIndex), so Store and a Choose-Weapon
        // level-up/Chest pick draw from the exact same random configuration - see
        // StoreUtility.RollWeaponOffers and docs/store-blacksmith.md.

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

        [Header("Accessory Service - Repair/Replacement Pricing")]
        [Tooltip("Cost to repair the buyer's AccessoryGuard straight back to full, indexed by how many durability points are MISSING (element 0 = 1 missing, element 1 = 2 missing, ...). Deliberately explicit per-step costs rather than a formula - see ResolveAccessoryRepairCost. Past the authored range the last entry holds, same convention TalentRarityTuning/SurvivalConfig.Phases already use.")]
        public FP[] AccessoryRepairCostByMissingDurability = { 25, 50 };

        [Tooltip("Cost to REPLACE a Broken (0 durability) AccessoryGuard. Must be higher than any repair cost - a total loss should never be the cheap option.")]
        public FP AccessoryBrokenReplacementCost = 100;

        // Repair always restores directly to AccessoryGuardConfig.BaseDurability - this only picks
        // WHAT that costs, never how much durability is bought (see docs/accessory-guard.md: no
        // per-point purchases, one clear Shop decision). `missing` is always >= 1 here; a full
        // accessory resolves to AccessoryServiceKind.None long before this is reached. Lives here
        // rather than on AccessoryGuardConfig because it's Store pricing, same as every other
        // offer/service this config prices - AccessoryGuardConfig stays Store-agnostic (durability,
        // pop/pickup) and is referenced by both Survival and Break for the mechanic itself.
        public FP ResolveAccessoryRepairCost(int missing)
        {
            if (AccessoryRepairCostByMissingDurability == null || AccessoryRepairCostByMissingDurability.Length == 0)
                return FP._0;

            int index = missing - 1;
            index = index < 0 ? 0 : index;
            index = index < AccessoryRepairCostByMissingDurability.Length ? index : AccessoryRepairCostByMissingDurability.Length - 1;

            return AccessoryRepairCostByMissingDurability[index];
        }

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

#if UNITY_EDITOR
        // The one invariant this asset can actually get wrong in authoring: "more damaged -> more
        // expensive", and "replacement > any repair". Editor-only, same reasoning DirectorConfig's
        // own authoring guardrail documents - a designer finds out while typing the number, not
        // three Breaks into a playtest. Moved here with the pricing fields themselves when the
        // Accessory service's cost moved off AccessoryGuardConfig onto its Store offer.
        private void OnValidate()
        {
            if (AccessoryRepairCostByMissingDurability == null)
                return;

            for (int i = 1; i < AccessoryRepairCostByMissingDurability.Length; i++)
            {
                if (AccessoryRepairCostByMissingDurability[i] >= AccessoryRepairCostByMissingDurability[i - 1])
                    continue;

                Debug.LogWarning($"[Store] {name}: AccessoryRepairCostByMissingDurability[{i}] ({AccessoryRepairCostByMissingDurability[i]}) " +
                                 $"is cheaper than [{i - 1}] ({AccessoryRepairCostByMissingDurability[i - 1]}) - a MORE damaged accessory should never cost LESS to restore.", this);
            }

            for (int i = 0; i < AccessoryRepairCostByMissingDurability.Length; i++)
            {
                if (AccessoryBrokenReplacementCost > AccessoryRepairCostByMissingDurability[i])
                    continue;

                Debug.LogWarning($"[Store] {name}: AccessoryBrokenReplacementCost ({AccessoryBrokenReplacementCost}) is not higher than " +
                                 $"AccessoryRepairCostByMissingDurability[{i}] ({AccessoryRepairCostByMissingDurability[i]}) - replacing a broken accessory should cost more than repairing a damaged one.", this);
            }
        }
#endif
    }
}
