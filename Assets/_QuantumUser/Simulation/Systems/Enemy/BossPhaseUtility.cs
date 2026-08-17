namespace Quantum
{
    using Photon.Deterministic;

    // Reads BossPhaseData.Modifiers for whichever phase a boss entity currently sits in - the "these
    // would need to scale reads at the point of use" half of BossStatModifiers' own comment
    // (BossDataAsset.cs), previously authored-but-unread. Not folded into EnemyDecisionUtility (which
    // already resolves BossDataAsset/BossRuntimeState for action-pool eligibility) since callers here
    // don't have an action-selection context to reuse - EnemySystem's own moveSpeed line, for
    // instance, has no Enemy* action in scope at all.
    public static unsafe class BossPhaseUtility
    {
        // FP default (0) means "unauthored" for every BossStatModifiers field, not "always zero this
        // stat out" - same "<=0 means off/unset" convention BossDataAsset.RetargetInterval and
        // StaggerProfileData.Threshold already use elsewhere in this file. A plain non-boss enemy, a
        // boss with no Phases authored yet, or a phase that simply didn't author this particular
        // multiplier all resolve to FP._1 (no-op) rather than silently freezing movement.
        public static FP ResolveMoveSpeedMultiplier(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, data, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.MoveSpeedMultiplier > FP._0 ? modifiers.MoveSpeedMultiplier : FP._1;
        }

        // Read from EnemySystem.UpdatePreparation's own StateTimer decrement, alongside (multiplied
        // together with) StatusEffectUtility.GetAnticipationMultiplier - see BossStatModifiers.
        // AnticipationMultiplier's own comment for why Telegraph needs no separate handling here.
        public static FP ResolveAnticipationMultiplier(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, data, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.AnticipationMultiplier > FP._0 ? modifiers.AnticipationMultiplier : FP._1;
        }

        private static bool TryGetCurrentPhaseModifiers(Frame f, EntityRef entity, EnemyDataAsset data, out BossStatModifiers modifiers)
        {
            modifiers = default;

            BossDataAsset bossData = data as BossDataAsset;
            if (bossData == null)
                return false;

            if (f.Unsafe.TryGetPointer<BossRuntimeState>(entity, out BossRuntimeState* boss) == false)
                return false;

            if (boss->CurrentPhaseIndex < 0 || boss->CurrentPhaseIndex >= bossData.Phases.Count)
                return false;

            modifiers = bossData.Phases[boss->CurrentPhaseIndex].Modifiers;
            return true;
        }
    }
}
