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

        [Header("Merchant Service - Repair Costs")]
        [Tooltip("Cost to repair straight back to full, indexed by how many durability points are MISSING (element 0 = 1 missing, element 1 = 2 missing, ...). Deliberately explicit per-step costs rather than a formula - see ResolveRepairCost. Past the authored range the last entry holds, same convention StoreConfig.BreakWeaponConfig/SurvivalConfig.Phases already use.")]
        public FP[] RepairCostByMissingDurability = { 25, 50 };

        [Tooltip("Cost to REPLACE a Broken (0 durability) accessory. Must be higher than any repair cost - a total loss should never be the cheap option.")]
        public FP BrokenReplacementCost = 100;

        // Repair always restores directly to BaseDurability - this only picks WHAT that costs, never
        // how much durability is bought (see docs/accessory-guard.md: no per-point purchases, one
        // clear Shop decision). `missing` is always >= 1 here; a full accessory resolves to
        // AccessoryServiceKind.None long before this is reached.
        public FP ResolveRepairCost(int missing)
        {
            if (RepairCostByMissingDurability == null || RepairCostByMissingDurability.Length == 0)
                return FP._0;

            int index = missing - 1;
            index = index < 0 ? 0 : index;
            index = index < RepairCostByMissingDurability.Length ? index : RepairCostByMissingDurability.Length - 1;

            return RepairCostByMissingDurability[index];
        }

#if UNITY_EDITOR
        // The one invariant this asset can actually get wrong in authoring: "more damaged -> more
        // expensive", and "replacement > any repair". Editor-only, same reasoning DirectorConfig's
        // own authoring guardrail documents - a designer finds out while typing the number, not
        // three Breaks into a playtest.
        private void OnValidate()
        {
            if (RepairCostByMissingDurability == null)
                return;

            for (int i = 1; i < RepairCostByMissingDurability.Length; i++)
            {
                if (RepairCostByMissingDurability[i] >= RepairCostByMissingDurability[i - 1])
                    continue;

                Debug.LogWarning($"[AccessoryGuard] {name}: RepairCostByMissingDurability[{i}] ({RepairCostByMissingDurability[i]}) " +
                                 $"is cheaper than [{i - 1}] ({RepairCostByMissingDurability[i - 1]}) - a MORE damaged accessory should never cost LESS to restore.", this);
            }

            for (int i = 0; i < RepairCostByMissingDurability.Length; i++)
            {
                if (BrokenReplacementCost > RepairCostByMissingDurability[i])
                    continue;

                Debug.LogWarning($"[AccessoryGuard] {name}: BrokenReplacementCost ({BrokenReplacementCost}) is not higher than " +
                                 $"RepairCostByMissingDurability[{i}] ({RepairCostByMissingDurability[i]}) - replacing a broken accessory should cost more than repairing a damaged one.", this);
            }
        }
#endif
    }
}
