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
        Advance1Min,            // + 60s of SurvivalTime
        AdvancePhase,           // jump to the next SurvivalConfig.Phases[] entry
        AdvanceToNextBreathing, // jump forward to the next Breathing phase
        LevelUp,                // grant exactly enough XP to earn one level (opens the upgrade screen)
        GetWeapon,              // AssetId = WeaponDataAsset guid
        GetRiftMutation,        // AssetId = RiftMutationData guid
        GrantGlobalUpgrade,     // AssetId = GlobalUpgradeData guid
        BuyAccessory,           // restore the sender's Accessory Guard to full
        GrantCoins,             // Amount = coins granted to the sender
        ToggleGodMode,          // add/remove the sender's Invulnerable tag
        KillAllEnemies,         // credit the sender (drops XP/coins as a normal kill would)
        HealFull,               // sender to full health
        OpenChest,              // open a Chest upgrade screen for the sender
        Revive                  // revive every Downed/KO player
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
