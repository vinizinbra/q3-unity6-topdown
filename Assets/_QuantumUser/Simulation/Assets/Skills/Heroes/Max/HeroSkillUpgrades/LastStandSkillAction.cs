namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Overdrive rank 1 - Unshaken (rank 1, keep Rage stacks on taking damage instead of resetting to
    // 0 - see RageRetentionUpgrade/RageOverdriveUtility.ResetStacks), Retaliation (rank 2, a brief
    // Weapon Damage buff whenever Max takes damage during Overdrive - see
    // MaxOverdriveReactionSystem), Too Angry to Die (rank 3, folds in the live CheatDeathGuard
    // lethal-save mechanism - NOT the same mechanism as the separate, now-deleted Adrenaline-based
    // "Too Angry to Die" passive that shared its name; see docs/max-ascensions.md's own note
    // resolving that collision). Fires on every Berserk Begin, same "refresh fresh off the live rank
    // each cast" idiom Brute's MomentumSkillAction already established - every granted component here
    // is safe to leave equipped between activations, since every reaction that reads them already
    // gates on Overdrive actually being active (RageOverdrive's own presence, or
    // CheatDeathUtility/OverdriveUtility's own SkillState.Active check) - no Begin/End pairing needed.
    public unsafe partial class LastStandSkillAction : SkillActionData
    {
        [Header("Rank 2 - Retaliation")]
        public FP RetaliationDuration = 2;
        public FP RetaliationDamageBonus = FP.FromString("0.20");

        [Header("Rank 3 - Too Angry to Die")]
        public FP CheatDeathImmunityDuration = 2;

        public LastStandSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));

            f.AddOrGet<RageRetentionUpgrade>(filter.Entity, out _);

            f.AddOrGet<LastStandUpgrade>(filter.Entity, out var lastStand);
            lastStand->HasRetaliation = rank >= 2;
            lastStand->RetaliationDuration = RetaliationDuration;
            lastStand->RetaliationDamageBonus = RetaliationDamageBonus;

            if (rank >= 3)
            {
                f.AddOrGet<CheatDeathGuard>(filter.Entity, out var guard);
                guard->ImmunityDuration = CheatDeathImmunityDuration;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
