namespace Quantum
{
    // Grant path for LevelUpPoolKind.GlobalUpgrade - see LevelUpUtility.GrantOption. Dispatches
    // generically to whichever GlobalUpgradeData subtype was picked, same as WeaponPerkData.Apply
    // is dispatched from WeaponSystem - this utility only resolves the asset, it never needs to
    // know which concrete effect it is.
    public static class GlobalUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            GlobalUpgradeData upgrade = f.FindAsset(upgradeRef);
            upgrade.Apply(f, entity);
        }
    }
}
