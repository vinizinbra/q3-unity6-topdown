namespace Quantum
{
    using Photon.Deterministic;

    // Reads BossPhaseData.Modifiers for whichever phase a boss entity currently sits in - the "these
    // would need to scale reads at the point of use" half of BossStatModifiers' own comment
    // (BossDataAsset.cs), previously authored-but-unread. Not folded into EnemyDecisionUtility (which
    // already resolves BossDataAsset/BossRuntimeState for action-pool eligibility) since callers here
    // don't have an action-selection context to reuse - EnemySystem's own moveSpeed line, for
    // instance, has no Enemy* action in scope at all.
    //
    // Every resolver only takes (Frame, EntityRef) and resolves EnemyDataAsset itself off the
    // entity's own Enemy->EnemyData - some call sites (StatusEffectUtility.GetLocalTimeMultiplier,
    // ProjectileSpawner.Spawn) only ever have the entity in scope, not a pre-resolved EnemyDataAsset,
    // so keeping every resolver self-contained means all of them are callable from anywhere with no
    // extra plumbing.
    public static unsafe class BossPhaseUtility
    {
        // FP default (0) means "unauthored" for every BossStatModifiers field, not "always zero this
        // stat out" - same "<=0 means off/unset" convention BossDataAsset.RetargetInterval and
        // StaggerProfileData.Threshold already use elsewhere in this file. A plain non-boss enemy, a
        // boss with no Phases authored yet, or a phase that simply didn't author this particular
        // multiplier all resolve to FP._1 (no-op) rather than silently freezing movement.
        public static FP ResolveMoveSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.MoveSpeedMultiplier > FP._0 ? modifiers.MoveSpeedMultiplier : FP._1;
        }

        // Read from EnemySystem.UpdatePreparation's own StateTimer decrement, alongside (multiplied
        // together with) StatusEffectUtility.GetAnticipationMultiplier - see BossStatModifiers.
        // AnticipationMultiplier's own comment for why Telegraph needs no separate handling here.
        public static FP ResolveAnticipationMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.AnticipationMultiplier > FP._0 ? modifiers.AnticipationMultiplier : FP._1;
        }

        // Folded into StatusEffectUtility.GetLocalTimeMultiplier itself - see BossStatModifiers.
        // ActiveSpeedMultiplier's own comment for why that single method is the one funnel point
        // every Active-phase delivery's StateTimer decrement already reads.
        public static FP ResolveActiveSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.ActiveSpeedMultiplier > FP._0 ? modifiers.ActiveSpeedMultiplier : FP._1;
        }

        // Read from ProjectileSpawner.Spawn alongside the existing (player-only) StatUtility.
        // GetProjectileSpeedMultiplier.
        public static FP ResolveProjectileSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.ProjectileSpeedMultiplier > FP._0 ? modifiers.ProjectileSpeedMultiplier : FP._1;
        }

        // Read from EnemySystem.UpdateRecovery's StateTimer decrement.
        public static FP ResolveRecoveryMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.RecoveryMultiplier > FP._0 ? modifiers.RecoveryMultiplier : FP._1;
        }

        // Read by each delivery that loops a fixed repeat count (ShellCount/PointCount/PelletCount/
        // scatter Count) at the point it resolves that count, and by SpawnPackDeliveryData to repeat
        // its whole authored Composition roster.
        public static FP ResolveQuantityMultiplier(Frame f, EntityRef entity)
        {
            if (TryGetCurrentPhaseModifiers(f, entity, out BossStatModifiers modifiers) == false)
                return FP._1;

            return modifiers.QuantityMultiplier > FP._0 ? modifiers.QuantityMultiplier : FP._1;
        }

        private static bool TryGetCurrentPhaseModifiers(Frame f, EntityRef entity, out BossStatModifiers modifiers)
        {
            modifiers = default;

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out Enemy* enemy) == false)
                return false;

            BossDataAsset bossData = f.FindAsset(enemy->EnemyData) as BossDataAsset;
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
