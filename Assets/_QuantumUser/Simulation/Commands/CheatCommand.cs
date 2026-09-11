namespace Quantum
{
    using Photon.Deterministic;

    // Which cheat a single CheatCommand carries. One command for every cheat (rather than one class
    // per cheat) so the whole feature stays two files - this + CheatSystem - plus the View overlay
    // that sends it (View/Managers/CheatMenu.cs). AssetId/Amount are only read by the actions whose
    // comment names them; every other action ignores them.
    public enum CheatActionKind : byte
    {
        Pause,                  // disables GameplaySystemGroup (freezes gameplay sim)
        Continue,               // re-enables GameplaySystemGroup
        Advance1Min,            // walks the Director's phase timeline forward by 60 real seconds
                                 // (PhaseTimer/CurrentPhaseIndex/SurvivalTime all together, crossing
                                 // phase boundaries as needed - see CheatSystem.AdvanceSurvivalClock)
        Advance30Sec,           // same as Advance1Min, 30 real seconds instead of 60
        AdvancePhase,           // jump to the next SurvivalConfig.Phases[] entry
        AdvanceToNextBreathing, // jump forward to the next Breathing phase
        LevelUp,                // grant exactly enough XP to earn one level (opens the upgrade screen)
        GetWeapon,              // AssetId = WeaponDataAsset guid
        GetRiftMutation,        // AssetId = RiftMutationData guid
        GrantGlobalUpgrade,     // AssetId = GlobalUpgradeData guid
        GrantPassiveUpgrade,    // AssetId = PassiveUpgradeData guid (per-hero pool - also covers
                                 // ranked Hero Mastery Ascensions, which are just PassiveUpgradeData
                                 // entries - see PassiveUpgradeUtility/docs/hero-mastery.md)
        GrantSkillUpgrade,      // AssetId = SkillActionData guid, Amount = SkillSlotId (1 = DashSkill,
                                 // 2 = HeroSkill) - covers both Dash Skill and Hero Skill upgrades
        BuyAccessory,           // restore the sender's Accessory Guard to full
        GrantCoins,             // Amount = coins granted to the sender
        ToggleGodMode,          // add/remove the sender's Invulnerable tag
        KillAllEnemies,         // credit the sender (drops XP/coins as a normal kill would)
        HealFull,               // sender to full health
        OpenChest,              // open a Chest upgrade screen for the sender
        Revive,                 // revive every Downed/KO player
        SetDamageToOne,         // set the sender's equipped Weapon.DamageMultiplier so live damage rounds to 1
        ResetDamage,            // reset the sender's equipped Weapon.DamageMultiplier back to 1 (baseline)
        ToggleManualFire,       // flip the sender's Weapon.CheatManualFire (auto-shoot off <-> on)
        JumpToBreathing,        // Amount = 1-4, the Nth Breathing-kind SurvivalConfig phase; also tops
                                 // TotalExperience up to that breath's paired display level if under it
        SetupTestRun            // Amount = 1-4, same breath number as JumpToBreathing - one-click
                                 // "midgame test setup" combo: JumpToBreathing(Amount), then instead
                                 // of leaving the level-ups that jump queues to be clicked through one
                                 // at a time, auto-resolves the whole queue synchronously right here;
                                 // also reveals the whole minimap (every Chunk.Discovered = true) and
                                 // tops the sender up to 5000 coins
    }

    // Generic debug/cheat command. IMPORTANT: this command AND its handler (CheatSystem) compile on
    // EVERY client unconditionally - a DeterministicCommand's factory index must match across all
    // clients or command serialization desyncs, and the effect must run on every client or the
    // cheat only lands on the sender and desyncs. Only the UI that SENDS these (CheatMenu, gated by
    // the CHEATS_ENABLED define) is strippable: once a command is on the wire every client applies
    // it identically, staying deterministic. Same shape as the existing GrantRiftMutationCommand.
    public unsafe class CheatCommand : DeterministicCommand
    {
        public CheatActionKind Action;
        public long AssetId; // AssetRef.Id.Value for the asset-carrying actions; 0 otherwise
        public int Amount;   // generic scalar payload (e.g. coins to grant)

        public override void Serialize(BitStream stream)
        {
            byte action = (byte)Action;
            stream.Serialize(ref action);
            Action = (CheatActionKind)action;

            stream.Serialize(ref AssetId);
            stream.Serialize(ref Amount);
        }
    }
}
