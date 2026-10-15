namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Overdrive line 1 - "Max refuses to go down". Overdrive starts at max Rage and hits drain it,
    // so this line rewards holding max Rage and protects it:
    //  - Rank 1: hits cost a smaller fraction of current Rage (LastStandUpgrade.RageLossFraction, the
    //    lower of it and Overdrive's baseline wins - see RageOverdriveUtility.ResetStacks), plus flat
    //    Damage Reduction while at max Rage (MaxAscensionUtility.ApplyLastStand/RevertLastStand).
    //  - Rank 2: the first hit of each Overdrive is fully blocked - a self-granted Free Hit Guard for
    //    the activation's Duration, negated before any damage signal fires, so it costs no Rage
    //    either. BerserkSkillData.End clears it if it's still up. Damage Reduction rises.
    //  - Rank 3 "Too Angry to Die": folds in the live CheatDeathGuard lethal-save mechanism - clamps
    //    to 1 HP, force-ends Overdrive, consumes Rage, and opens a brief Invulnerable window (see
    //    CheatDeathUtility.TryPreventLethal, which cannot recursively retrigger inside that window
    //    since DamageUtility ignores every hit outright while Invulnerable is present).
    //
    // Fires on every Berserk Begin, same "refresh fresh off the live rank each cast" idiom Brute's
    // MomentumSkillAction already established - every granted component here is safe to leave
    // equipped between activations, since every reaction that reads them already gates on Overdrive
    // actually being active (RageOverdrive's own presence, or CheatDeathUtility/OverdriveUtility's own
    // SkillState.Active check) - no Begin/End pairing needed.
    public unsafe partial class LastStandSkillAction : SkillActionData
    {
        [Header("Rage resilience")]
        [Tooltip("Fraction of CURRENT Rage removed when Max takes damage during Overdrive, per rank. Only applies when lower than BerserkSkillData.RageLossFraction.")]
        public FP[] RageLossFraction = { FP._0_25, FP._0_25, FP._0_25 };

        [Tooltip("Flat Damage Reduction while at max Rage during Overdrive, per rank.")]
        public FP[] MaxRageDamageReduction = { FP.FromString("0.15"), FP._0_25, FP._0_25 };

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
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<LastStandUpgrade>(filter.Entity, out var lastStand);
            lastStand->RageLossFraction = RageLossFraction[index];
            lastStand->MaxRageDamageReduction = MaxRageDamageReduction[index];
            lastStand->BlocksFirstHit = rank >= 2;

            // Overdrive starts at max Rage, so there is no later threshold crossing for
            // RageOverdriveUtility to hook - react to the state we're actually in. ApplyLastStand
            // latches on AppliedDamageReduction, so this can't double-apply.
            if (RageOverdriveUtility.IsAtMaxRage(f, filter.Entity) == true)
            {
                MaxAscensionUtility.ApplyLastStand(f, filter.Entity, lastStand);
            }

            // slot->StateTimer is the Duration BerserkSkillData.Begin just set - the guard covers the
            // base activation; End clears it early if it's still standing.
            if (lastStand->BlocksFirstHit == true)
            {
                StatusEffectUtility.ApplyFreeHitGuard(f, filter.Entity, filter.Entity, slot->StateTimer);
            }

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
