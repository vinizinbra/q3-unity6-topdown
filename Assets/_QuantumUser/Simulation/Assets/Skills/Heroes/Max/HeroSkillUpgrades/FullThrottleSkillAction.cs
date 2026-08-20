namespace Quantum
{
    using Photon.Deterministic;

    // Overdrive line 2 - a Weapon Damage/Reload Speed buff active only while Overdrive is active AND
    // Rage is genuinely maxed (RageOverdriveUtility.IsAtMaxRage) - toggled exactly at that threshold
    // by RageOverdriveUtility.EnterMaxRage/ResetStacks via MaxAscensionUtility.ApplyFullThrottle/
    // RevertFullThrottle, not active for the whole Overdrive window. Rank 3 additionally refills the
    // magazine ONCE on that same threshold crossing (FullThrottleUpgrade.HasInstantReload ->
    // WeaponSystem.RefillMagazine), replacing the old always-free-reload-while-maxed tag - see that
    // component's own comment.
    // Fires on every Berserk Begin, same "refresh fresh off the live rank each cast" idiom Brute's
    // MomentumSkillAction already established.
    public unsafe partial class FullThrottleSkillAction : SkillActionData
    {
        public FP[] WeaponDamageBonus = { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };
        public FP[] ReloadSpeedBonus = { FP._0, FP._0_50, FP._0_50 };

        public FullThrottleSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<FullThrottleUpgrade>(filter.Entity, out var fullThrottle);
            fullThrottle->WeaponDamageBonus = WeaponDamageBonus[index];
            fullThrottle->ReloadSpeedBonus = ReloadSpeedBonus[index];
            fullThrottle->HasInstantReload = rank >= 3;

            // Last Stand rank 1 can start an activation already at max Rage, in which case there is
            // no later threshold crossing for RageOverdriveUtility to hook - so react to the state
            // we're actually in. ApplyFullThrottle latches on Applied, so this can't double-apply.
            if (RageOverdriveUtility.IsAtMaxRage(f, filter.Entity) == true)
            {
                MaxAscensionUtility.ApplyFullThrottle(f, filter.Entity, fullThrottle);
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
