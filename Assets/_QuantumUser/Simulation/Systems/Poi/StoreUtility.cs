namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Store's own interaction - see Store.qtn/docs/store-blacksmith.md. Mirrors CursedRiftUtility's
    // overall shape (ResolveInteractionState/TryBeginInteraction/command-driven mutators), but
    // Store has no multi-stage session - StoreInteraction just marks "this player's Store window is
    // open," any number of Buy commands can land while it's present.
    public static unsafe class StoreUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution and Busy already happened there via the sibling Interactable component) - the
        // richer WHY behind whether the button would do anything right now. Store has no
        // PoiUsagePolicy/AlreadyUsed state of its own (see Store.qtn) - it's always browsable while
        // Available.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef store)
        {
            if (f.Unsafe.TryGetPointer<Store>(store, out var storeComponent) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, storeComponent->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-validates in full (never trusts the View/target resolution alone,
        // same reasoning CursedRiftUtility.TryBeginInteraction documents), rolls this Break's
        // inventory if it hasn't been rolled yet, then opens the window.
        public static void TryBeginInteraction(Frame f, EntityRef player, EntityRef store)
        {
            if (f.Has<StoreInteraction>(player) == true)
                return;

            if (ResolveInteractionState(f, player, store) != ContextInteractionState.Available)
                return;

            if (f.RuntimeConfig.StoreConfig.IsValid == false)
            {
                Log.Error("[Store] interaction requested but RuntimeConfig has no StoreConfig assigned - ignored");
                return;
            }

            StoreConfig config = f.FindAsset(f.RuntimeConfig.StoreConfig);
            EnsureInventoryRolled(f, player, store, config);

            f.AddOrGet<StoreInteraction>(player, out var interaction);
            interaction->Store = store;

            Log.Debug($"[Store] {player} opened {store}");
        }

        // Called from StoreSystem when a CloseStoreCommand lands.
        public static void Close(Frame f, EntityRef player)
        {
            f.Remove<StoreInteraction>(player);
        }

        // Rolls this Breathing Break's WeaponOffers/FoodOffers exactly once, regardless of which
        // (or how many) players trigger it - idempotent re-check against RolledAtBreathingIndex, so
        // a second/third player opening the same Store this same Break is a no-op here. The
        // triggering player's own RuntimePlayer.Talents.WeaponLevel drives the weapon offers'
        // quality (see RollWeaponOffers) - whoever opens the Store first this Break sets it for
        // every player until the next restock, an accepted consequence of the inventory itself
        // being shared rather than per-player. Deterministic across clients: f.Filter/f.RNG both
        // already guarantee this, same as every other shared roll in this codebase (e.g.
        // CombatDirectorUtility's own spawn rolls).
        private static void EnsureInventoryRolled(Frame f, EntityRef player, EntityRef store, StoreConfig config)
        {
            // AddOrGet's own return value means "false only if the entity doesn't exist" - NOT
            // "was this newly added" - so a fresh StoreInventory can't be detected that way. Checked
            // via TryGetPointer first instead (same idiom PoiUsageUtility's own "no record yet"
            // checks use), so the -1 sentinel is seeded exactly once, before AddOrGet's own
            // zero-init would otherwise leave it at a false-matching 0 (a real bug: Global.
            // BreathingIndex also starts at 0, so a fresh 0 default silently equals it and skips
            // the very first roll of a run entirely - exactly what was showing 0 items).
            bool hasInventory = f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory);

            if (hasInventory == false)
            {
                f.AddOrGet<StoreInventory>(store, out inventory);
                inventory->RolledAtBreathingIndex = -1;
            }

            if (inventory->RolledAtBreathingIndex == f.Global->BreathingIndex)
                return;

            RollWeaponOffers(f, player, inventory, config);
            RollFoodOffers(f, inventory, config);
            inventory->RolledAtBreathingIndex = f.Global->BreathingIndex;

            Log.Debug($"[Store] {store} restocked for BreathingIndex {f.Global->BreathingIndex} - {inventory->WeaponOfferCount} weapon(s), {inventory->FoodOfferCount} food offer(s)");
        }

        // Distinct weapons drawn the same uniform-without-replacement way
        // LevelUpUtility.RollChooseWeaponOptionsFor already does. Two independent axes drive a Store
        // offer's own quality: Global.SurvivalTime sets Weapon Level + starting perk COUNT via
        // LevelUpConfig.ResolveWeaponOfferLevel/RollWeaponOfferPerkCount - the exact same shared roll
        // a Choose-Weapon level-up/Chest pick uses (see docs/store-blacksmith.md) - while the
        // triggering player's own RuntimePlayer.Talents.WeaponLevel (the SAME persistent
        // meta-progression stat that seeds CharacterStats.WeaponTalentLevel at spawn, see
        // PlayerSpawnUtility.Spawn/docs/talents.md - deliberately NOT the live in-run
        // CharacterStats.WeaponTalentLevel, which is pure bookkeeping now) sets perk RARITY
        // (StoreConfig.ResolveTalentRarityTuning/RollStorePerks).
        private static void RollWeaponOffers(Frame f, EntityRef player, StoreInventory* inventory, StoreConfig config)
        {
            var offers = inventory->WeaponOffers;

            for (int i = 0; i < offers.Length; i++)
                offers[i] = default;

            inventory->WeaponOfferCount = 0;

            if (config.WeaponPool.IsValid == false || f.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                Log.Error("[Store] StoreConfig.WeaponPool or RuntimeConfig.LevelUpConfig not assigned - no weapon offers rolled");
                return;
            }

            WeaponChoicePoolData pool = f.FindAsset(config.WeaponPool);
            LevelUpConfig levelUpConfig = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            byte weaponTalentLevel = ResolveWeaponLevelTalent(f, player);
            FP survivalSeconds = f.Global->SurvivalTime;
            byte weaponLevel = levelUpConfig.ResolveWeaponOfferLevel(survivalSeconds);

            int poolCount = pool.Weapons.Count;
            int slots = config.MaxWeaponOfferSlots < offers.Length ? config.MaxWeaponOfferSlots : offers.Length;
            slots = slots < poolCount ? slots : poolCount;

            if (slots <= 0)
                return;

            bool* taken = stackalloc bool[poolCount];
            int drawn = 0;

            for (int slot = 0; slot < slots; slot++)
            {
                int roll = f.RNG->Next(0, poolCount);

                while (taken[roll] == true)
                {
                    roll = (roll + 1) % poolCount;
                }

                taken[roll] = true;

                int perkCount = levelUpConfig.RollWeaponOfferPerkCount(f, survivalSeconds);
                AssetRef<WeaponPerkData>[] rolledPerks = RollStorePerks(f, levelUpConfig.WeaponPerkPool, perkCount, config,
                    weaponTalentLevel, pool.Weapons[roll]);

                StoreWeaponOffer offer = default;
                offer.WeaponData = pool.Weapons[roll];
                offer.WeaponLevel = weaponLevel;
                offer.RolledPerkCount = (byte)rolledPerks.Length;

                var offerPerks = offer.RolledPerks;
                for (int p = 0; p < offerPerks.Length; p++)
                    offerPerks[p] = p < rolledPerks.Length ? rolledPerks[p] : default;

                offer.Price = config.WeaponOfferBasePrice + config.WeaponOfferPricePerPerk * offer.RolledPerkCount;

                offers[drawn] = offer;
                drawn++;
            }

            inventory->WeaponOfferCount = (byte)drawn;
        }

        // Weighted draw WITHOUT REPLACEMENT (WeightedDrawUtility) from the same WeaponPerkPool a
        // Choose-Weapon level-up pick already draws from (LevelUpConfig.WeaponPerkPool) - weighted
        // by StoreConfig.TalentRarityTuning (the buyer's own account-level Weapon Talent Level)
        // rather than WeaponPerkPoolData's own flat Common/Rare/Epic/LegendaryWeight fields, mirrors
        // BlacksmithUtility.RollPerkOptions' exact shape. One candidate per distinct perk asset, so
        // "without replacement" is what guarantees a freshly-rolled weapon can never receive the
        // same perk twice (no AlreadyEquipped exclusion needed here - unlike Blacksmith, this is a
        // brand new weapon with no perks yet).
        private static AssetRef<WeaponPerkData>[] RollStorePerks(Frame f, AssetRef<WeaponPerkPoolData> poolRef, int perkCount,
            StoreConfig config, byte weaponTalentLevel, AssetRef<WeaponDataAsset> weaponRef)
        {
            if (perkCount <= 0 || poolRef.IsValid == false)
                return System.Array.Empty<AssetRef<WeaponPerkData>>();

            WeaponPerkPoolData pool = f.FindAsset(poolRef);
            WeaponTalentRarityTuning tuning = config.ResolveTalentRarityTuning(weaponTalentLevel);

            // The offer's own weapon, not the buyer's current one - these perks are rolled onto the
            // weapon being sold. See WeaponPerkData.SupportsFireType.
            WeaponFireType fireType = WeaponGenerator.ResolveFireType(f, weaponRef);

            List<WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>>> candidates = new List<WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>>>();

            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (perkRef.IsValid == false)
                    continue;

                WeaponPerkData data = f.FindAsset(perkRef);

                if (data.SupportsFireType(fireType) == false)
                    continue;

                int weight = tuning.GetWeight(data.Rarity);

                if (weight <= 0)
                    continue;

                candidates.Add(new WeightedDrawUtility.Candidate<AssetRef<WeaponPerkData>> { Value = perkRef, Weight = weight });
            }

            return WeightedDrawUtility.Draw(f, candidates, perkCount);
        }

        // RuntimePlayer.Talents lives outside the deterministic Frame state proper (it's the raw
        // per-player join payload, same "carried in from outside this match" data
        // PlayerSpawnUtility.Spawn already reads once via f.GetPlayerData) - resolved via this
        // entity's own PlayerLink -> PlayerRef, same lookup pattern used everywhere else in this
        // codebase that needs a player's own RuntimePlayer outside of spawn time.
        private static byte ResolveWeaponLevelTalent(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<PlayerLink>(player, out var playerLink) == false)
                return 0;

            RuntimePlayer runtimePlayer = f.GetPlayerData(playerLink->Player);
            return runtimePlayer != null ? runtimePlayer.Talents.WeaponLevel : (byte)0;
        }

        private static void RollFoodOffers(Frame f, StoreInventory* inventory, StoreConfig config)
        {
            var offers = inventory->FoodOffers;

            for (int i = 0; i < offers.Length; i++)
                offers[i] = default;

            inventory->FoodOfferCount = 0;

            if (config.FoodPool.IsValid == false)
            {
                Log.Error("[Store] StoreConfig.FoodPool not assigned - no food offers rolled");
                return;
            }

            FoodOfferPoolData pool = f.FindAsset(config.FoodPool);
            List<WeightedDrawUtility.Candidate<AssetRef<FoodOfferData>>> candidates = new List<WeightedDrawUtility.Candidate<AssetRef<FoodOfferData>>>();

            for (int i = 0; i < pool.Foods.Count; i++)
            {
                AssetRef<FoodOfferData> foodRef = pool.Foods[i];

                if (foodRef.IsValid == false)
                    continue;

                FoodOfferData data = f.FindAsset(foodRef);
                candidates.Add(new WeightedDrawUtility.Candidate<AssetRef<FoodOfferData>> { Value = foodRef, Weight = data.Weight });
            }

            int slots = config.FoodOfferCount < offers.Length ? config.FoodOfferCount : offers.Length;
            AssetRef<FoodOfferData>[] rolled = WeightedDrawUtility.Draw(f, candidates, slots);

            for (int i = 0; i < rolled.Length; i++)
            {
                FoodOfferData data = f.FindAsset(rolled[i]);
                offers[i] = new StoreFoodOffer { Food = rolled[i], Price = data.Price };
            }

            inventory->FoodOfferCount = (byte)rolled.Length;
        }

        // How many of the shared StoreInventory.WeaponOffers slots THIS player can actually buy
        // from - confirmed with the user: ShopWeaponOfferCount (a meta-progression talent, see
        // docs/talents.md) maps rank0 -> 1 offer, rank1 -> 2, rank2 -> 3, i.e. a flat +1 offset,
        // clamped to however many the shared inventory actually rolled (which itself never exceeds
        // StoreConfig.MaxWeaponOfferSlots).
        public static int ResolveWeaponOfferCount(Frame f, EntityRef player, EntityRef store, StoreConfig config)
        {
            int rolledCount = f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory)
                ? inventory->WeaponOfferCount
                : config.MaxWeaponOfferSlots;

            int talentOfferCount = 1;

            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == true)
                talentOfferCount = stats->ShopWeaponOfferCount + 1;

            int max = config.MaxWeaponOfferSlots < rolledCount ? config.MaxWeaponOfferSlots : rolledCount;
            return talentOfferCount < max ? talentOfferCount : max;
        }

        // Called from StoreSystem when a BuyStoreWeaponCommand lands - re-validates everything
        // server-side (never trusts the View), spends Coins, then grants the exact same way a
        // Choose-Weapon level-up option does (WeaponChoiceUtility.Grant, reused unchanged) by
        // constructing a throwaway LevelUpOption from this offer's own 3 relevant fields.
        public static void BuyWeapon(Frame f, EntityRef player, StoreInteraction* interaction, int offerIndex)
        {
            if (f.RuntimeConfig.StoreConfig.IsValid == false || f.RuntimeConfig.LevelUpConfig.IsValid == false)
                return;

            StoreConfig config = f.FindAsset(f.RuntimeConfig.StoreConfig);
            LevelUpConfig levelUpConfig = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            EntityRef store = interaction->Store;

            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
                return;

            int offerCount = ResolveWeaponOfferCount(f, player, store, config);

            if (offerIndex < 0 || offerIndex >= offerCount || offerIndex >= inventory->WeaponOfferCount)
            {
                Log.Error($"[Store] {player} sent BuyWeapon OfferIndex {offerIndex}, outside their own eligible 0-{offerCount - 1} - ignored");
                return;
            }

            if (IsPurchased(f, player, store, offerIndex, isWeaponOffer: true) == true)
            {
                Log.Debug($"[Store] {player} already bought weapon offer {offerIndex} this Break - ignored");
                return;
            }

            StoreWeaponOffer offer = inventory->WeaponOffers[offerIndex];

            if (CoinUtility.TrySpend(f, player, offer.Price) == false)
            {
                Log.Debug($"[Store] {player} can't afford weapon offer {offerIndex} ({offer.Price} Coins)");
                return;
            }

            LevelUpOption option = default;
            option.Kind = LevelUpPoolKind.ChooseWeapon;
            option.WeaponData = offer.WeaponData;
            option.RolledPerkCount = offer.RolledPerkCount;
            option.RolledWeaponLevel = offer.WeaponLevel;

            var optionPerks = option.RolledPerks;
            var offerPerks = offer.RolledPerks;
            for (int i = 0; i < optionPerks.Length; i++)
                optionPerks[i] = i < offerPerks.Length ? offerPerks[i] : default;

            WeaponChoiceUtility.Grant(f, player, option, levelUpConfig.WeaponLevelDamageBonusPerLevel);
            MarkPurchased(f, player, store, offerIndex, isWeaponOffer: true);

            Log.Debug($"[Store] {player} bought weapon offer {offerIndex} ({offer.WeaponData}) for {offer.Price} Coins, starting at Weapon Level {offer.WeaponLevel}");
        }

        // Called from StoreSystem when a BuyStoreFoodCommand lands - same re-validate/spend/apply
        // shape as BuyWeapon, applied instantly (no food inventory - see docs/store-blacksmith.md's
        // MVP scope).
        public static void BuyFood(Frame f, EntityRef player, StoreInteraction* interaction, int offerIndex)
        {
            EntityRef store = interaction->Store;

            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
                return;

            if (offerIndex < 0 || offerIndex >= inventory->FoodOfferCount)
            {
                Log.Error($"[Store] {player} sent BuyFood OfferIndex {offerIndex}, outside 0-{inventory->FoodOfferCount - 1} - ignored");
                return;
            }

            if (IsPurchased(f, player, store, offerIndex, isWeaponOffer: false) == true)
            {
                Log.Debug($"[Store] {player} already bought food offer {offerIndex} this Break - ignored");
                return;
            }

            StoreFoodOffer offer = inventory->FoodOffers[offerIndex];

            if (offer.Food.IsValid == false)
                return;

            if (CoinUtility.TrySpend(f, player, offer.Price) == false)
            {
                Log.Debug($"[Store] {player} can't afford food offer {offerIndex} ({offer.Price} Coins)");
                return;
            }

            FoodOfferData data = f.FindAsset(offer.Food);
            data.Apply(f, player);
            MarkPurchased(f, player, store, offerIndex, isWeaponOffer: false);

            Log.Debug($"[Store] {player} bought food offer {offerIndex} ({offer.Food}) for {offer.Price} Coins");
        }

        // "Increase Weapon Level" - a guaranteed offer, always present, never rolled into
        // StoreInventory (see StoreConfig's own comment). Price scales with the buyer's OWN
        // currently-equipped Weapon.Level, live - not cached anywhere, same "read live, never
        // baked" idiom every other Store price already follows.
        public static FP ResolveWeaponLevelUpPrice(Frame f, EntityRef player, StoreConfig config)
        {
            byte level = f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == true ? weapon->Level : (byte)0;
            return config.WeaponLevelUpBasePrice + config.WeaponLevelUpPricePerLevel * level;
        }

        // See StorePurchases.WeaponLevelUpPurchasedAtBreathingIndexPlusOne's own comment for why
        // this is stored offset by 1 rather than compared directly against BreathingIndex.
        public static bool IsWeaponLevelUpPurchased(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<StorePurchases>(player, out var purchases) == false)
                return false;

            return purchases->WeaponLevelUpPurchasedAtBreathingIndexPlusOne == f.Global->BreathingIndex + 1;
        }

        // Called from StoreSystem when a BuyStoreWeaponLevelCommand lands - re-validates
        // affordability/once-per-Break server-side same as every other Store purchase, spends
        // Coins, then levels up the buyer's own equipped Weapon directly (WeaponSystem.AddLevel) -
        // no WeaponChoiceUtility.Grant involved, this doesn't touch CharacterStats.WeaponTalentLevel
        // or grant a new weapon, only the one already equipped.
        public static void BuyWeaponLevelUp(Frame f, EntityRef player)
        {
            if (f.RuntimeConfig.StoreConfig.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false)
            {
                Log.Error($"[Store] {player} sent BuyWeaponLevelUp but has no equipped Weapon - ignored");
                return;
            }

            if (IsWeaponLevelUpPurchased(f, player) == true)
            {
                Log.Debug($"[Store] {player} already bought a weapon level up this Break - ignored");
                return;
            }

            StoreConfig config = f.FindAsset(f.RuntimeConfig.StoreConfig);
            FP price = ResolveWeaponLevelUpPrice(f, player, config);

            if (CoinUtility.TrySpend(f, player, price) == false)
            {
                Log.Debug($"[Store] {player} can't afford a weapon level up ({price} Coins)");
                return;
            }

            WeaponSystem.AddLevel(weapon, config.WeaponLevelUpDamageBonusPerLevel);
            MarkWeaponLevelUpPurchased(f, player);

            Log.Debug($"[Store] {player} bought a weapon level up (now Level {weapon->Level}) for {price} Coins");
        }

        private static void MarkWeaponLevelUpPurchased(Frame f, EntityRef player)
        {
            f.AddOrGet<StorePurchases>(player, out var purchases);
            purchases->WeaponLevelUpPurchasedAtBreathingIndexPlusOne = f.Global->BreathingIndex + 1;
        }

        // Per-offer purchase tracking - NOT PoiUsage (that's one bit per whole POI, see Store.qtn's
        // own comment). Find-and-overwrite-in-place by (Store, OfferIndex, IsWeaponOffer), same
        // idiom PoiUsageUtility.MarkUsed already uses for its own array - so a later inventory roll
        // reusing the same (Store, OfferIndex) slot for a NEW item correctly reads as unpurchased
        // again (PurchasedAtBreathingIndex no longer matches the offer's own live roll index).
        public static bool IsPurchased(Frame f, EntityRef player, EntityRef store, int offerIndex, bool isWeaponOffer)
        {
            if (f.Unsafe.TryGetPointer<StorePurchases>(player, out var purchases) == false)
                return false;

            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
                return false;

            var entries = purchases->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Store != store || entries[i].OfferIndex != offerIndex || entries[i].IsWeaponOffer != isWeaponOffer)
                    continue;

                return entries[i].PurchasedAtBreathingIndex == inventory->RolledAtBreathingIndex;
            }

            return false;
        }

        private static void MarkPurchased(Frame f, EntityRef player, EntityRef store, int offerIndex, bool isWeaponOffer)
        {
            if (f.Unsafe.TryGetPointer<StoreInventory>(store, out var inventory) == false)
                return;

            f.AddOrGet<StorePurchases>(player, out var purchases);
            var entries = purchases->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Store != store || entries[i].OfferIndex != offerIndex || entries[i].IsWeaponOffer != isWeaponOffer)
                    continue;

                StorePurchaseEntry entry = entries[i];
                entry.PurchasedAtBreathingIndex = inventory->RolledAtBreathingIndex;
                entries[i] = entry;
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Store != EntityRef.None)
                    continue;

                entries[i] = new StorePurchaseEntry
                {
                    Store = store,
                    OfferIndex = (byte)offerIndex,
                    IsWeaponOffer = isWeaponOffer,
                    PurchasedAtBreathingIndex = inventory->RolledAtBreathingIndex
                };
                return;
            }

            Log.Error($"[Store] {player} has no free StorePurchases slot - purchase won't be tracked (treat as a headroom bug)");
        }
    }
}
