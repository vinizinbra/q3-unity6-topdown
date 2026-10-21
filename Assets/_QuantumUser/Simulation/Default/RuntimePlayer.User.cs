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

        // This player's meta-progression - carried in from OUTSIDE this match (MatchMakingConfig.
        // AddLocalPlayers -> TalentSaveMapper.ApplyPlayer, from the local save) and never written by
        // the simulation. Mostly seeded once into CharacterStats at spawn (PlayerSpawnUtility.Spawn);
        // a few lines (Weapons, Revive, Co-op) are also read mid-match via TalentUtility.TryGetTalents. A struct (not a class) so this field is never null - every
        // RuntimePlayer gets an all-zero PlayerTalents for free, no separate initialization needed.
        public PlayerTalents Talents;
    }

    // See RuntimePlayer.Talents' own comment above for the shared "seeded once from outside, never
    // written by the simulation" contract every field here follows. Power/Exploration mirror the menu
    // Upgrades tab (PermanentUpgradeId sections) and hold purchased LEVELS only, written by the menu's
    // TalentSaveMapper from the local save - what each level is worth lives in TalentsConfig (resolved by
    // TalentUtility at spawn), so a number is tuned in exactly one place. Exploration's WORLD lines
    // (Store, Gunsmith, ...) are the leader's and live on RuntimeConfig.WorldUnlocks instead.
    [System.Serializable]
    public struct PlayerTalents
    {
        public PowerTalents Power;
        public ExplorationTalents Exploration;
    }

    // Menu Power lines (0-5) - baked into this player's own CharacterStats multipliers at spawn
    // (PlayerSpawnUtility.Spawn -> TalentUtility.ApplyPerPlayerTalents).
    [System.Serializable]
    public struct PowerTalents
    {
        public byte DamageLevel;
        public byte SkillCooldownLevel;
        public byte FireRateLevel;
        public byte ReloadSpeedLevel;
        public byte CriticalChanceLevel;
        public byte CriticalDamageLevel;
        public byte MaxHealthLevel;
        public byte MaxShieldLevel;
        public byte DamageReductionLevel;
        public byte MoveSpeedLevel;
        public byte PickupRangeLevel;
        public byte XpGainLevel;
        public byte CoinGainLevel;
    }

    // Menu Exploration per-player lines.
    [System.Serializable]
    public struct ExplorationTalents
    {
        // For the whole run, every weapon THIS player equips gets TalentsConfig.WeaponLevelBonusByLevel
        // extra levels (WeaponSystem.ApplyMetaWeaponLevels, on every Equip + the starting weapon at spawn),
        // and every newly rolled weapon - Store, chest or level-up alike - rolls perks on
        // TalentsConfig.WeaponPerkRarityByLevel. Also seeds CharacterStats.WeaponTalentLevel (bookkeeping only).
        public byte WeaponsLevel;
        public byte StartingGoldLevel;          // TalentsConfig.StartingGoldByLevel Coins at spawn
        public byte RerollsLevel;               // TalentsConfig.RerollsByLevel -> CharacterStats.RerollQuantity
        public byte ReviveLevel;                // self-revive charges/recovery/guard
        public byte CoopLevel;                  // rescue speed/downed time/rescue HP
    }
}
