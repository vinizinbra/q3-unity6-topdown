namespace Quantum
{
    using Photon.Deterministic;

    // The Break half of the Recoverable Accessory Guard - a Merchant SERVICE, sold on the Store's
    // own screen alongside its weapon/food offers (see docs/accessory-guard.md/docs/store-blacksmith.md).
    //
    // Modelled directly on Store's own guaranteed "Increase Weapon Level" offer: not part of the
    // shared, rolled StoreInventory (nothing random about it), price resolved live off the buyer's
    // own state, and bought with its own dedicated command. That's also what keeps it off the weapon
    // purchase allowance - StoreUtility.ResolveWeaponOfferCount and StorePurchases.Entries are only
    // ever consulted for rolled offers, and this touches neither.
    //
    // Every method is per-PLAYER: two co-op players standing at the same Merchant independently
    // resolve their own AccessoryGuard, so one can be offered a Repair, one a Replacement and one
    // nothing at all, in the same Break.
    public static unsafe class AccessoryServiceUtility
    {
        // What (if anything) this player can buy right now, derived purely from their own missing
        // durability - never from which hero they are, and never from the accessory's world state.
        // A Dropped accessory still lying out in the level is deliberately serviceable: paying for a
        // restore reconciles it (AccessoryGuardUtility.Restore destroys the outstanding collectible),
        // rather than the shop refusing to help until the player walks back for it.
        public static AccessoryServiceKind ResolveService(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard) == false)
                return AccessoryServiceKind.None;

            // Last Bastion traded the mechanic away entirely - there is nothing to sell this player.
            // Stated explicitly rather than relying on Disable() also zeroing MaxDurability below,
            // so the intent survives any future change to how a disabled guard is represented.
            if (guard->Disabled == true)
                return AccessoryServiceKind.None;

            if (guard->MaxDurability == 0)
                return AccessoryServiceKind.None;

            if (guard->CurrentDurability == 0)
                return AccessoryServiceKind.Replacement;

            if (guard->CurrentDurability >= guard->MaxDurability)
                return AccessoryServiceKind.None;

            return AccessoryServiceKind.Repair;
        }

        // Explicit per-step prices out of AccessoryGuardConfig - deliberately no dynamic formula
        // (see that asset's own comment). More missing durability costs more; a Broken replacement
        // costs more than any repair, enforced by an authoring guardrail on the config itself rather
        // than clamped at runtime.
        public static FP ResolvePrice(Frame f, EntityRef player)
        {
            if (AccessoryGuardUtility.TryGetConfig(f, out AccessoryGuardConfig config) == false)
                return FP._0;

            if (f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard) == false)
                return FP._0;

            switch (ResolveService(f, player))
            {
                case AccessoryServiceKind.Replacement:
                    return config.BrokenReplacementCost;

                case AccessoryServiceKind.Repair:
                    return config.ResolveRepairCost(AccessoryGuardUtility.GetMissingDurability(guard));

                default:
                    return FP._0;
            }
        }

        public static bool CanAfford(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                return false;

            return stats->Coins >= ResolvePrice(f, player);
        }

        // Called from StoreSystem when a BuyAccessoryServiceCommand lands. Re-validates everything
        // simulation-side (never trusts the View), same as every other Store purchase.
        //
        // There is no once-per-Break purchase tracking here, and deliberately so: a successful
        // service always restores to FULL, which immediately resolves the player to
        // AccessoryServiceKind.None - the state itself is the limit, so a second purchase this Break
        // is impossible without first losing durability again. That also means declining costs
        // nothing and changes nothing: a player who walks away at 1/3 simply starts the next
        // Survival at 1/3 (docs/accessory-guard.md's own "strategic consequence").
        public static void TryPurchaseService(Frame f, EntityRef player)
        {
            AccessoryServiceKind kind = ResolveService(f, player);

            if (kind == AccessoryServiceKind.None)
            {
                Log.Debug($"[Accessory] {player} sent BuyAccessoryService with nothing to restore - ignored");
                return;
            }

            // The service is a Merchant service, so it needs the buyer to actually be at an open
            // Store - the same re-validation StoreUtility.BuyWeaponLevelUp performs by only ever
            // being reachable through a live StoreInteraction (see StoreSystem's own gate).
            if (f.Has<StoreInteraction>(player) == false)
                return;

            FP price = ResolvePrice(f, player);

            if (CoinUtility.TrySpend(f, player, price) == false)
            {
                Log.Debug($"[Accessory] {player} can't afford an accessory {kind} ({price} Coins)");
                return;
            }

            AccessoryGuardUtility.Restore(f, player);

            if (f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard) == false)
                return;

            f.Events.AccessoryRestored(player, kind == AccessoryServiceKind.Replacement, guard->CurrentDurability);

            Log.Debug($"[Accessory] {player} bought an accessory {kind} for {price} Coins -> {guard->CurrentDurability}/{guard->MaxDurability}");
        }
    }
}
