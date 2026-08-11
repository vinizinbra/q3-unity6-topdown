namespace Quantum
{
    // Grant path for LevelUpPoolKind.PassiveUpgrade - see LevelUpUtility.GrantOption. Dispatches
    // generically to whichever PassiveUpgradeData subtype was picked, same as
    // GlobalUpgradeUtility.Grant - this utility only resolves the asset, it never needs to know
    // which concrete effect it is.
    public static unsafe class PassiveUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            PassiveUpgradeData upgrade = f.FindAsset(upgradeRef);

            if (upgrade.MaxRank > 1)
                upgrade.Apply(f, entity, GetRank(f, entity, upgradeRef) + 1);
            else
                upgrade.Apply(f, entity);
        }

        // How many times this entity has already picked this Passive Upgrade - see
        // UpgradeHistoryUtility.GetCount, the shared read side of UpgradeHistory both Passive Upgrade
        // and Skill Upgrade ranks are tracked through.
        public static int GetRank(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef) =>
            UpgradeHistoryUtility.GetCount(f, entity, LevelUpPoolKind.PassiveUpgrade, new AssetRef<UpgradeData>(upgradeRef.Id));

        // Read by LevelUpUtility.CollectPerHeroCandidates to exclude a Passive Upgrade from every
        // future roll for this entity once it's been picked MaxRank times - offering it again would
        // just be a dead card, same reasoning IsCappedOut/AlreadyGranted/RiftMutationUtility.
        // IsAlreadyPicked already use elsewhere in LevelUpUtility. MaxRank defaults to 1, so this is a
        // pure boolean "already picked" check for every non-ranked passive, unchanged from before
        // ranking existed.
        public static bool IsAlreadyPicked(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            PassiveUpgradeData upgrade = f.FindAsset(upgradeRef);
            return GetRank(f, entity, upgradeRef) >= upgrade.MaxRank;
        }
    }
}
