namespace Quantum
{
    // Rank-read side for LevelUpPoolKind.SkillUpgrade - mirrors PassiveUpgradeUtility.GetRank/
    // IsAlreadyPicked, both backed by the same UpgradeHistoryUtility.GetCount. Grant/eligibility for
    // this pool live in SkillSystem.AddUpgrade and LevelUpUtility.AlreadyGranted (SkillSlot.Upgrades
    // has no per-entry Count field, unlike GlobalUpgradePicks - see SkillActionData.MaxRank's own
    // comment for why rank stays a live UpgradeHistory lookup instead of a SkillSlot change).
    public static class SkillUpgradeUtility
    {
        public static int GetRank(Frame f, EntityRef entity, AssetRef<SkillActionData> actionRef) =>
            UpgradeHistoryUtility.GetCount(f, entity, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(actionRef.Id));

        public static bool IsCappedOut(Frame f, EntityRef entity, AssetRef<SkillActionData> actionRef)
        {
            SkillActionData action = f.FindAsset(actionRef);
            return GetRank(f, entity, actionRef) >= action.MaxRank;
        }
    }
}
