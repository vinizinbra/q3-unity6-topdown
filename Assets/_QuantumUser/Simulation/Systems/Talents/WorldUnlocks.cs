namespace Quantum
{
    // World-feature meta-progression levels (menu Upgrades tab > Exploration world lines) carried on
    // RuntimeConfig, not RuntimePlayer: they decide what the level GENERATES, and generation runs from
    // frame 0, before any RuntimePlayer has arrived. Online they are the party leader's levels (sent with
    // SyncMatchRoom next to difficulty/world, MatchMakingConfig); offline the local save's. Read only
    // through WorldUnlockUtility. See docs/talents.md, "Meta progression".
    [System.Serializable]
    public struct WorldUnlocks
    {
        // False (an Inspector-authored RuntimeConfig, e.g. playing the gameplay scene directly) = every
        // world feature counts as fully unlocked, so local testing keeps its POIs. The menu sets it true.
        public bool Enforce;

        public byte StoreLevel;
        public byte GunsmithLevel;
        public byte ChallengesLevel;
        public byte HealingShrineLevel;
        public byte SacrificeRiftLevel;
        public byte RiftAnchorLevel;
    }

    public enum WorldUnlock : byte
    {
        None,
        Store,
        Gunsmith,
        Challenges,
        HealingShrine,
        SacrificeRift,
        RiftAnchor
    }

    public static class WorldUnlockUtility
    {
        // Level of a world line this match. Not enforced = treated as maxed (byte.MaxValue).
        public static int GetLevel(Frame f, WorldUnlock unlock)
        {
            WorldUnlocks unlocks = f.RuntimeConfig.WorldUnlocks;
            if (unlocks.Enforce == false)
                return byte.MaxValue;

            switch (unlock)
            {
                case WorldUnlock.Store: return unlocks.StoreLevel;
                case WorldUnlock.Gunsmith: return unlocks.GunsmithLevel;
                case WorldUnlock.Challenges: return unlocks.ChallengesLevel;
                case WorldUnlock.HealingShrine: return unlocks.HealingShrineLevel;
                case WorldUnlock.SacrificeRift: return unlocks.SacrificeRiftLevel;
                case WorldUnlock.RiftAnchor: return unlocks.RiftAnchorLevel;
                default: return byte.MaxValue;
            }
        }

        // minLevel <= 0 reads as 1 ("unlocked at all").
        public static bool IsUnlocked(Frame f, WorldUnlock unlock, int minLevel)
        {
            return unlock == WorldUnlock.None || GetLevel(f, unlock) >= (minLevel < 1 ? 1 : minLevel);
        }

        public static bool IsUnlocked(Frame f, in ChunkPoolEntry entry)
        {
            return IsUnlocked(f, entry.Unlock, entry.UnlockMinLevel);
        }
    }
}

namespace Quantum
{
    // Which LEVEL of each menu Upgrades line turns a behaviour on - mirrors the per-level cards in
    // ExplorationUpgradesGenerator (the card text is the spec). Amounts (how much heal, how many Coins)
    // live in TalentsConfig; only "from which level" lives here, since it's tied to the card order.
    public static class MetaUnlockLevels
    {
        // World lines (RuntimeConfig.WorldUnlocks)
        public const int StoreRestock = 2;           // Store L2 "Store Restock"
        public const int StoreExtraWeaponOffer = 3;  // Store L3 "Expanded Arsenal"
        public const int GunsmithRestock = 2;        // Gunsmith L2 "Gunsmith Restock"
        public const int GunsmithMasterCraftsman = 3;// Gunsmith L3 "Master Craftsman"
        public const int GunsmithReroll = 4;         // Gunsmith L4 "Gunsmith Reroll"
        public const int TraversalChallenge = 1;     // Challenges L1
        public const int TeamChallenge = 2;          // Challenges L2
        public const int ShrineImprovedHealing = 2;  // Healing Shrine L2
        public const int ShrineFasterRecovery = 3;   // Healing Shrine L3
        public const int SacrificeChoice = 2;        // Sacrifice Rift L2
        public const int BaseWeaponChest = 1;        // Rift Anchor L1
        public const int BaseRepairStation = 2;      // Rift Anchor L2
        public const int BaseChestGuaranteedPerk = 3;// Rift Anchor L3

        // Per-player lines (RuntimePlayer.Talents)
        public const int ReviveRecovery = 2;         // Revive L2
        public const int ReviveGuard = 3;            // Revive L3
        public const int CoopFastRescue = 1;         // Co-op L1
        public const int CoopLastStand = 2;          // Co-op L2
        public const int CoopRescueRecovery = 3;     // Co-op L3
    }
}
