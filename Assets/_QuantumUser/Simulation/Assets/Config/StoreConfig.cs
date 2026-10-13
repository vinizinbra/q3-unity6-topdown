namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

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

        // Starting perk RARITY isn't authored here either: every newly rolled weapon (Store, chest, level-up)
        // uses TalentsConfig.WeaponPerkRarityByLevel by the buyer's Weapons line level - see
        // LevelUpUtility.RollWeaponOption.

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
        [Tooltip("Cost of ONE durability point on a damaged (not broken) AccessoryGuard. The service is sold one point at a time - buy again for the next point, until full.")]
        public FP AccessoryRepairCostPerPoint = 25;

        [Tooltip("Cost to REPLACE a Broken (0 durability) AccessoryGuard - comes back at 1 durability; further points are bought at AccessoryRepairCostPerPoint. Must be higher than a repair point - a total loss should never be the cheap option.")]
        public FP AccessoryBrokenReplacementCost = 50;

        // Total Coins to go from `current` durability back to `max` buying one point at a time -
        // the replacement price for the first point when broken, AccessoryRepairCostPerPoint for
        // every point after. Used by the Balance Simulator / previews, never by the purchase path.
        public FP ResolveAccessoryFullRestoreCost(int current, int max)
        {
            if (current >= max)
                return FP._0;

            if (current <= 0)
                return AccessoryBrokenReplacementCost + AccessoryRepairCostPerPoint * (max - 1);

            return AccessoryRepairCostPerPoint * (max - current);
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
        // The one invariant this asset can actually get wrong in authoring: "replacement > a repair
        // point". Editor-only, same reasoning DirectorConfig's own authoring guardrail documents - a
        // designer finds out while typing the number, not three Breaks into a playtest.
        private void OnValidate()
        {
            if (AccessoryBrokenReplacementCost > AccessoryRepairCostPerPoint)
                return;

            Debug.LogWarning($"[Store] {name}: AccessoryBrokenReplacementCost ({AccessoryBrokenReplacementCost}) is not higher than " +
                             $"AccessoryRepairCostPerPoint ({AccessoryRepairCostPerPoint}) - replacing a broken accessory should cost more than repairing a point.", this);
        }
#endif
    }
}
