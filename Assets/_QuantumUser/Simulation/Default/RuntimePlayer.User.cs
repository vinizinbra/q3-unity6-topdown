using Photon.Deterministic;

namespace Quantum
{
    public partial class RuntimePlayer
    {
        public FP test;

        // Local-testing convenience (see docs/bots.md): this player slot is driven by the
        // simulation's own BotInputSystem instead of a real device, so one person can play a full
        // co-op party solo. Read exactly twice - PlayerSpawnUtility.Spawn (adds BotBrain, which is
        // what every simulation-side check actually keys off from then on) and the View's
        // QuantumHelper.GetLocalSlotIndex (which hides bots from every "is this a LOCAL player"
        // path, so a bot never claims a couch-co-op slot, a camera target, or a HUD binding).
        // Authored per entry on QuantumRunnerLocalDebug.LocalPlayers[] / MatchMakingConfig.
        // RuntimePlayers[], same "seeded once from outside the match" contract Talents below has.
        public bool IsBot;

        // Every field on this entity's own meta-progression - carried in from OUTSIDE this match
        // (see MatchMakingConfig.StartRunner, which reads it from local PlayerPrefs before
        // AddPlayer) and, with the sole exception of WeaponLevel below, seeded once into this
        // player's own CharacterStats at spawn (PlayerSpawnUtility.Spawn) - never written by the
        // simulation afterward. A struct (not a class) so this field is never null - every
        // RuntimePlayer gets an all-zero PlayerTalents for free, no separate initialization needed.
        public PlayerTalents Talents;
    }

    // See RuntimePlayer.Talents' own comment above for the shared "seeded once from outside, never
    // written by the simulation" contract every field here follows.
    [System.Serializable]
    public struct PlayerTalents
    {
        // Exploration "Weapons" line level (0-5), written by the menu's TalentSaveMapper. For the whole run,
        // every weapon THIS player equips gets TalentsConfig.WeaponLevelBonusByLevel extra levels
        // (WeaponSystem.ApplyMetaWeaponLevels, on every Equip + the starting weapon at spawn), and every newly
        // rolled weapon - Store, chest or level-up alike - rolls perks on TalentsConfig.WeaponPerkRarityByLevel.
        // Also seeds CharacterStats.WeaponTalentLevel (bookkeeping only).
        public byte WeaponLevel;

        // Reroll-charge talent - seeds CharacterStats.RerollQuantity once at spawn
        // (PlayerSpawnUtility.Spawn). Not one of the Player*Level fields below - those are all 0-5
        // percent-scaled multipliers (TalentUtility.ApplyPerPlayerTalents), while this is a raw flat
        // count copied 1:1, same shape as WeaponLevel -> WeaponTalentLevel. Distinct from
        // CharacterStats.RerollQuantity itself, which then only ever decreases for the rest of THIS
        // match as LevelUpUtility.RerollOptionsFor spends it - a reroll is a pre-run talent, not an
        // in-run-pickable Global Upgrade (see docs/level-up-upgrades.md's "Reroll" section).
        public byte RerollQuantity;

        // Store weapon-offer-count talent - seeds CharacterStats.ShopWeaponOfferCount once at spawn
        // (PlayerSpawnUtility.Spawn), same raw-flat-count shape as RerollQuantity above. Confirmed
        // with the user: rank0 -> 1 offer, rank1 -> 2, rank2 -> 3 (StoreUtility.
        // ResolveWeaponOfferCount reads it as +1, clamped to StoreConfig.MaxWeaponOfferSlots). See
        // docs/store-blacksmith.md.
        public byte ShopWeaponOfferCount;

        // Starting-Coins talent - seeds CharacterStats.Coins once at spawn (PlayerSpawnUtility.
        // Spawn), same raw-flat-value shape as RerollQuantity/ShopWeaponOfferCount above rather
        // than a percent-scaled Player*Level multiplier - a head start on THIS run's own per-player
        // Coin wallet (docs/breathing-poi.md's own currency-conversion section), not a permanent
        // stat bonus. int (not byte) since this is a real currency amount, not a small 0-5 level -
        // CharacterStats.Coins itself is FP, effectively unbounded.
        public int StartingCoins;

        // Player* fields are baked into this player's own CharacterStats at spawn
        // (PlayerSpawnUtility.Spawn -> TalentUtility.ApplyPerPlayerTalents). Has*/Can* fields are
        // OR'd across every connected player (TalentUtility.ComputeSharedTalents) to decide what
        // exists for the whole co-op group in the LobbyStart chunk - see docs/talents.md.
        public byte PlayerDamageLevel;          // 0-5
        public byte PlayerCooldownLevel;        // 0-5
        public byte PlayerFireRateLevel;        // 0-5
        public byte PlayerReloadSpeedLevel;     // 0-5
        public byte PlayerCriticalChanceLevel;  // 0-5
        public byte PlayerCriticalDamageLevel;  // 0-5
        public byte PlayerMaxHealthLevel;       // 0-5
        public byte PlayerMaxShieldLevel;       // 0-5
        public byte PlayerDamageReductionLevel; // 0-5
        public byte PlayerMoveSpeedLevel;       // 0-5
        public byte PlayerPickupRangeLevel;     // 0-5
        public byte PlayerExperienceLevel;      // 0-5
        public bool HasWeaponChest;
        public bool HasHeroChest;
        public bool HasGlobalUpgradeChest;
        public bool HasUnlockedRift;            // scaffolded only - not yet consumed anywhere
        public bool CanFindStones;              // scaffolded only - not yet consumed anywhere
        public bool HasEvent;                   // scaffolded only - not yet consumed anywhere

        // Menu Upgrades tab (docs/talents.md, "Meta progression") - raw purchased LEVELS of the
        // per-player lines, written by the menu's TalentSaveMapper from the local save. What each
        // level is worth lives in TalentsConfig (resolved by TalentUtility at spawn), never here, so a
        // number is tuned in exactly one place. The raw count fields above (RerollQuantity,
        // StartingCoins, SelfReviveCharges...) stay as Inspector-only test extras added on top.
        public byte PlayerCoinGainLevel;        // Power: Coin Gain
        public byte StartingGoldLevel;          // Exploration: Starting Gold
        public byte RerollsLevel;               // Exploration: Rerolls
        public byte ReviveLevel;                // Exploration: Revive (self-revive charges/recovery/guard)
        public byte CoopLevel;                  // Exploration: Co-op (rescue speed/downed time/rescue HP)
    }
}