namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Overdrive line 1 - Max's "getting hit costs momentum" weakness, softened one rank at a time:
    //  - Rank 1: Rage PERSISTS between Overdrive activations (LastStandUpgrade.PersistsRage) - the
    //    Rage live at End is parked and handed straight back at the next Begin, so an Overdrive can
    //    now start already spun up instead of always from 0. See BerserkSkillData.Begin/End.
    //  - Rank 2: taking damage during Overdrive removes only RageLossFraction of the current Rage
    //    instead of wiping it (see RageOverdriveUtility.ResetStacks).
    //  - Rank 3 "Too Angry to Die": folds in the live CheatDeathGuard lethal-save mechanism - clamps
    //    to 1 HP, force-ends Overdrive, consumes Rage, and opens a brief Invulnerable window (see
    //    CheatDeathUtility.TryPreventLethal, which cannot recursively retrigger inside that window
    //    since DamageUtility ignores every hit outright while Invulnerable is present).
    //
    // Fires on every Berserk Begin, same "refresh fresh off the live rank each cast" idiom Brute's
    // MomentumSkillAction already established - every granted component here is safe to leave
    // equipped between activations, since every reaction that reads them already gates on Overdrive
    // actually being active (RageOverdrive's own presence, or CheatDeathUtility/OverdriveUtility's own
    // SkillState.Active check) - no Begin/End pairing needed. LastStandUpgrade specifically MUST
    // persist, since rank 1's parked Rage lives on it.
    public unsafe partial class LastStandSkillAction : SkillActionData
    {
        [Header("Rank 2 - reduced Rage disruption")]
        [Tooltip("Fraction of CURRENT Rage removed when Max takes damage during Overdrive, per rank. 1 = the unchanged baseline full reset.")]
        public FP[] RageLossFraction = { FP._1, FP._0_50, FP._0_50 };

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
            lastStand->PersistsRage = true;
            lastStand->RageLossFraction = RageLossFraction[index];

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
