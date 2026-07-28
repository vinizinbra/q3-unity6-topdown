namespace Quantum
{
    // Grant path for LevelUpPoolKind.GlobalUpgrade - see LevelUpUtility.GrantOption. No gameplay
    // effect is designed yet (see GlobalUpgradeData); this only exercises the level-up plumbing
    // end-to-end once a pool entry exists. Replace the log with the real effect once one is designed.
    public static class GlobalUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            Log.Debug($"[LevelUp] {entity} selected Global Upgrade {upgradeRef} - grant path not implemented yet");
        }
    }
}
