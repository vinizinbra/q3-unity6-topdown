namespace Quantum
{
    // Grant path for LevelUpPoolKind.GlobalUpgrade - see LevelUpUtility.GrantOption. Dispatches
    // generically to whichever GlobalUpgradeData subtype was picked, same as WeaponPerkData.Apply
    // is dispatched from WeaponSystem - this utility only resolves the asset, it never needs to
    // know which concrete effect it is.
    public static unsafe class GlobalUpgradeUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            GlobalUpgradeData upgrade = f.FindAsset(upgradeRef);
            upgrade.Apply(f, entity);

            // Only capped upgrades (MaxPicks > 0) bother tracking pick history at all - see
            // GlobalUpgradeData.MaxPicks and LevelUpUtility.CollectGlobalCandidates, the one place
            // that reads it back.
            if (upgrade.MaxPicks > 0)
            {
                RecordPick(f, entity, upgradeRef);
            }
        }

        // 0 (no GlobalUpgradePicks component, or no matching entry yet) correctly reads as "never
        // picked" - RecordPick below is the only thing that ever creates an entry.
        public static byte GetPickCount(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            if (f.Unsafe.TryGetPointer<GlobalUpgradePicks>(entity, out var picks) == false)
                return 0;

            var entries = picks->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Upgrade == upgradeRef)
                    return entries[i].Count;
            }

            return 0;
        }

        private static void RecordPick(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            f.AddOrGet<GlobalUpgradePicks>(entity, out var picks);
            var entries = picks->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Upgrade != upgradeRef)
                    continue;

                GlobalUpgradePickEntry entry = entries[i];
                entry.Count++;
                entries[i] = entry;
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Upgrade.IsValid == true)
                    continue;

                entries[i] = new GlobalUpgradePickEntry { Upgrade = upgradeRef, Count = 1 };
                return;
            }

            Log.Error($"[LevelUp] {entity} has no free GlobalUpgradePicks slot for {upgradeRef} - pick count not tracked, its MaxPicks cap won't be enforced");
        }
    }
}
