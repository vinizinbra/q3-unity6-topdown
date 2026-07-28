namespace Quantum
{
    // Grant path for LevelUpPoolKind.PassiveUpgrade - see LevelUpUtility.GrantOption. No gameplay
    // effect is designed yet (see PassiveUpgradeData); this only exercises the level-up plumbing
    // end-to-end once a pool entry exists. Replace the log with the real effect once one is designed.
    public static class PassiveUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            Log.Debug($"[LevelUp] {entity} selected Passive Upgrade {upgradeRef} - grant path not implemented yet");
        }
    }
}
