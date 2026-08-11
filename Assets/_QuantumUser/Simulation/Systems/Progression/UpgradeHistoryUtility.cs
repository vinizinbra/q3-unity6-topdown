namespace Quantum
{
    // Shared read side of UpgradeHistory (LevelUp.qtn) - LevelUpUtility.RecordHistory already
    // increments UpgradeHistoryEntry.Count correctly on every repeat pick, for any LevelUpPoolKind,
    // but nothing used to read it back as a count (only as boolean presence). This is the one place
    // that does, so PassiveUpgradeUtility.GetRank and SkillUpgradeUtility.GetRank (and
    // GameplayUiController.BuildCardData's generic rank-card UI) all share one implementation instead
    // of three copies of the same scan.
    public static unsafe class UpgradeHistoryUtility
    {
        public static int GetCount(Frame f, EntityRef entity, LevelUpPoolKind kind, AssetRef<UpgradeData> upgrade)
        {
            if (f.Unsafe.TryGetPointer<UpgradeHistory>(entity, out var history) == false)
                return 0;

            var entries = history->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Kind == kind && entries[i].Upgrade.Id == upgrade.Id)
                    return entries[i].Count;
            }

            return 0;
        }
    }
}
