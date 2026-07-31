namespace Quantum
{
    // Grant path for LevelUpPoolKind.PassiveUpgrade - see LevelUpUtility.GrantOption. Dispatches
    // generically to whichever PassiveUpgradeData subtype was picked, same as
    // GlobalUpgradeUtility.Grant - this utility only resolves the asset, it never needs to know
    // which concrete effect it is.
    public static class PassiveUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            PassiveUpgradeData upgrade = f.FindAsset(upgradeRef);
            upgrade.Apply(f, entity);
        }
    }
}
