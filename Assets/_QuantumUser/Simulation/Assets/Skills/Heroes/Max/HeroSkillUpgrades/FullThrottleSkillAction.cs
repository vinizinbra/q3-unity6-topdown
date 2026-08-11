namespace Quantum
{
    using Photon.Deterministic;

    // Overdrive rank 2 - a Weapon Damage/Reload Speed buff active only while Overdrive is active AND
    // Rage is genuinely maxed (RageOverdriveUtility.IsAtMaxRage) - toggled exactly at that threshold
    // by RageOverdriveUtility.TryAdvanceStack/ResetStacks via MaxAscensionUtility.ApplyFullThrottle/
    // RevertFullThrottle, not active for the whole Overdrive window. Rank 3 additionally grants the
    // existing InstantReloadOverdrive tag, repointing WeaponSystem's own IsInstantReloadOverdriven
    // check onto the same live max-Rage condition instead of the old standalone Instant Reload pick.
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

            if (rank >= 3)
            {
                f.AddOrGet<InstantReloadOverdrive>(filter.Entity, out _);
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
