namespace Quantum
{
    using Photon.Deterministic;

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

        public AssetRef<FoodOfferPoolData> FoodPool;
        public int FoodOfferCount = 2;
    }
}
