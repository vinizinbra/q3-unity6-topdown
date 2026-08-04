namespace Quantum
{
    // Grant path for LevelUpPoolKind.PassiveUpgrade - see LevelUpUtility.GrantOption. Dispatches
    // generically to whichever PassiveUpgradeData subtype was picked, same as
    // GlobalUpgradeUtility.Grant - this utility only resolves the asset, it never needs to know
    // which concrete effect it is. Every Passive Upgrade is single-pick (mirrors RiftMutationUtility's
    // shape, not GlobalUpgradeUtility's opt-in MaxPicks), so Grant always records the pick.
    public static unsafe class PassiveUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            PassiveUpgradeData upgrade = f.FindAsset(upgradeRef);
            upgrade.Apply(f, entity);
            RecordPick(f, entity, upgradeRef);
        }

        // Read by LevelUpUtility.CollectPerHeroCandidates to exclude an already-granted passive
        // upgrade from every future roll for this entity - offering it again would just be a dead
        // card, same reasoning IsCappedOut/AlreadyGranted/RiftMutationUtility.IsAlreadyPicked already
        // use elsewhere in LevelUpUtility.
        public static bool IsAlreadyPicked(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            if (f.Unsafe.TryGetPointer<PassiveUpgradePicks>(entity, out var picks) == false)
                return false;

            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i] == upgradeRef)
                    return true;
            }

            return false;
        }

        private static void RecordPick(Frame f, EntityRef entity, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            f.AddOrGet<PassiveUpgradePicks>(entity, out var picks);
            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i].IsValid == true)
                    continue;

                picked[i] = upgradeRef;
                return;
            }

            Log.Error($"[LevelUp] {entity} has no free PassiveUpgradePicks slot for {upgradeRef} - pick not recorded, it could be offered again");
        }
    }
}
