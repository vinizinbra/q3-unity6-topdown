namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Which Merchant service a given accessory qualifies for right now - resolved purely from
    // durability, never from which hero owns it (see AccessoryServiceUtility.ResolveService).
    public enum AccessoryServiceKind : byte
    {
        None,       // already at MaxDurability - nothing to sell
        Repair,     // damaged (1..Max-1) - restores straight to full
        Replacement // Broken (0) - restores straight to full, at a higher price
    }

    // Global tuning for the Recoverable Accessory Guard (see AccessoryGuard.qtn, AccessoryGuardUtility
    // and docs/accessory-guard.md) - referenced via RuntimeConfig.AccessoryGuardConfig. Deliberately
    // ONE asset covering both halves of the mechanic: the Survival half (durability, how the
    // accessory pops off and how it's recovered) and the Break half (what restoring it costs at the
    // Merchant). Hero-agnostic throughout - the per-hero half is presentation only and lives on
    // CharacterData.View.cs instead.
    //
    // The dropped collectible's own EntityPrototype is NOT here - it lives on
    // RuntimeConfig.Prefabs.DroppedAccessoryPrototype, same place every other spawned-from-config
    // pickup prototype (ExpOrb/Coin/RiftShard/Scrap) already lives.
    public class AccessoryGuardConfig : AssetObject
    {
        [Header("Durability")]
        [Tooltip("Durability every hero's accessory starts a run with, and what a Merchant repair/replacement restores it to. Each blocked hit costs exactly 1; reaching 0 breaks it. 0 disables the whole mechanic (nothing is seeded, nothing blocks).")]
        public byte BaseDurability = 3;

        [Tooltip("A hit dealing LESS than this passes straight through to Health and costs no durability - the guard only ever reacts to it. 0 (default) blocks every hit regardless of size, reproducing the original no-threshold behaviour. Exists so chip damage from Filler/Swarm enemies can't drain a durability point (worth real Coins to repair) for a hit that barely threatened the player's Health in the first place.")]
        public FP MinDamageToBlock = FP._0;

        [Header("Drop / Pop")]
        [Tooltip("Ring around the owner that candidate landing spots are sampled from at the moment of the block.")]
        public FP MinDropOffset = FP._1;
        public FP MaxDropOffset = 3;

        [Tooltip("How many candidate spots in that ring are tested for solid ground before giving up. The landing point is CHOSEN FIRST and the arc is then solved exactly onto it (see AccessoryGuardUtility.ResolveLandingPosition) - the accessory never flies blind and then gets corrected. Every candidate that finds no Ground-layer collider (open water, a pit, off the level edge) is rejected; if all of them fail, the accessory simply drops at the owner's feet, which is by definition solid ground.")]
        public int LandingSampleAttempts = 8;

        [Tooltip("Arc shape range. The launch ANGLE is what varies per drop, not the velocity - randomising velocity would break the solved arc and put the accessory somewhere other than the spot that was just validated. Steeper reads as popped straight up, shallower as knocked away.")]
        public FP MinLaunchAngle = 40;
        public FP MaxLaunchAngle = 65;

        [Tooltip("Let the popped accessory come to rest on ground HIGHER than the spot it flew from - a ledge, a platform. Deliberately the opposite of every currency drop (see PopVelocity.CanLandHigher), which is hard-blocked from climbing because a coin you have to go and climb for is a chore. Here the retrieval IS the mechanic, so an awkward landing is a feature. Uncheck to make it behave like a coin.")]
        public bool CanLandOnHigherGround = true;

        [Header("Recovery")]
        [Tooltip("Base collection radius, multiplied by the collecting player's own CharacterStats.PickupRangeMultiplier - same shape CurrencyOrbSystem uses for a coin.")]
        public FP PickupRadius = 1;

        [Tooltip("Let ANY player recover a dropped accessory and return it to its owner, not just the owner themselves (co-op). The spatial cost is preserved either way - somebody still had to physically travel to it - it just becomes a cost the team can share instead of one only the owner can pay. Untick to make an accessory owner-only, in which case a teammate walking over it does nothing.")]
        public bool AllowAllyRecovery = true;

        // A dropped accessory deliberately has NO lifetime field at all - it waits for its owner
        // indefinitely, unlike a currency orb (CoinConfig.OrbLifetime), since losing it permanently
        // to a timer would silently turn a recoverable resource into a broken one.

        // Merchant repair/replacement PRICING is no longer authored here - it moved to
        // StoreConfig.AccessoryRepairCostByMissingDurability/AccessoryBrokenReplacementCost (see
        // AccessoryServiceUtility.ResolvePrice), since it's Store pricing, same as every other
        // offer/service StoreConfig already prices. This asset stays Store-agnostic: durability,
        // pop/pickup - the mechanic itself, referenced by both Survival and Break.
    }
}
