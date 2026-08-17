namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Boss-only phase/stagger logic, kept out of EnemySystem itself (same reasoning as
    // JuggernautDischargeCooldownSystem/JuggernautLandingImpactSystem living in their own systems
    // rather than being folded into the generic AI shell). Only does anything for an entity that
    // actually has BossRuntimeState (see that component's own comment - opt-in, boss entities only).
    [Preserve]
    public unsafe class BossSystem : SystemMainThreadFilter<BossSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);
            BossDataAsset bossData = data as BossDataAsset;

            if (bossData == null)
            {
                Log.Error($"[Boss] {filter.Entity} has a BossRuntimeState component but its EnemyData isn't a BossDataAsset - nothing to drive");
                return;
            }

            TickPhase(f, ref filter, bossData);
            TickStagger(f, ref filter, bossData);
            TickComboChain(f, ref filter, bossData);
            TickRetarget(f, ref filter, bossData);
        }

        // Advances at most one phase per tick (never skips ahead even if multiple thresholds would
        // qualify at once) - phases are meant to be entered in authored order.
        private static void TickPhase(Frame f, ref Filter filter, BossDataAsset bossData)
        {
            if (bossData.Phases.Count == 0)
                return;

            int nextPhaseIndex = filter.BossRuntimeState->CurrentPhaseIndex + 1;

            if (nextPhaseIndex >= bossData.Phases.Count)
                return; // already in the last authored phase

            BossPhaseData nextPhase = bossData.Phases[nextPhaseIndex];
            bool shouldEnter = false;

            switch (nextPhase.EntryTrigger)
            {
                case BossPhaseEntryTrigger.HealthThreshold:
                    if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == true && health->MaxHealth > FP._0)
                    {
                        shouldEnter = health->CurrentHealth / health->MaxHealth <= nextPhase.HealthPercentThreshold;
                    }
                    break;

                case BossPhaseEntryTrigger.Timer:
                    filter.BossRuntimeState->PhaseTimer += f.DeltaTime;
                    shouldEnter = filter.BossRuntimeState->PhaseTimer >= nextPhase.TimerSeconds;
                    break;

                default:
                    // ArenaEvent/AddWaveCleared/Scripted - see BossPhaseEntryTrigger's own comment.
                    break;
            }

            if (shouldEnter == true)
            {
                filter.BossRuntimeState->CurrentPhaseIndex = nextPhaseIndex;
                filter.BossRuntimeState->PhaseTimer = FP._0;
                Log.Debug($"[Boss] {filter.Entity} entered phase {nextPhaseIndex} ({nextPhase.EntryTrigger})");
            }
        }

        // Builds StaggerMeter from damage taken since last tick (diffed off Health.CurrentHealth -
        // see BossRuntimeState's own comment for why, instead of a dedicated on-damaged signal),
        // drains it at RegenRate otherwise, and forces OnBreakForcedAction once it crosses
        // Threshold.
        private static void TickStagger(Frame f, ref Filter filter, BossDataAsset bossData)
        {
            if (bossData.Stagger.Threshold <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == false)
                return;

            FP lastHealth = filter.BossRuntimeState->LastObservedHealth;

            // First tick after spawn has nothing to diff against yet - seed the baseline instead of
            // reading a damage spike out of the initial (0 -> MaxHealth) jump.
            if (lastHealth <= FP._0)
            {
                filter.BossRuntimeState->LastObservedHealth = health->CurrentHealth;
                return;
            }

            FP damageTaken = lastHealth - health->CurrentHealth;
            filter.BossRuntimeState->LastObservedHealth = health->CurrentHealth;

            if (damageTaken > FP._0)
            {
                filter.BossRuntimeState->StaggerMeter += damageTaken;
            }
            else
            {
                filter.BossRuntimeState->StaggerMeter = FPMath.Max(FP._0, filter.BossRuntimeState->StaggerMeter - bossData.Stagger.RegenRate * f.DeltaTime);
            }

            if (filter.BossRuntimeState->StaggerMeter >= bossData.Stagger.Threshold)
            {
                filter.BossRuntimeState->StaggerMeter = FP._0;
                ForceBreakAction(f, ref filter, bossData);
            }
        }

        // Hard override - doesn't call the current delivery's EnemyDeliveryData.OnInterrupted
        // (unlike EnemySystem.CancelActive), since a stagger break is a guaranteed scripted moment,
        // not a conditional interrupt. Known gap: state left behind by whatever the boss was
        // mid-delivery on (e.g. a kinematic charge) isn't cleaned up - not exercised by any boss
        // yet, revisit if one needs it.
        private static void ForceBreakAction(Frame f, ref Filter filter, BossDataAsset bossData)
        {
            if (bossData.Stagger.OnBreakForcedAction.IsValid == false)
                return;

            int skillIndex = FindSkillSlot(bossData, bossData.Stagger.OnBreakForcedAction);

            if (skillIndex < 0)
            {
                Log.Error($"[Boss] {filter.Entity}'s OnBreakForcedAction isn't listed in SkillActions - can't resolve a cooldown slot for it");
                return;
            }

            EnemyActionData action = f.FindAsset(bossData.Stagger.OnBreakForcedAction);

            filter.Enemy->CurrentActionSlot = (byte)(skillIndex + 1);
            filter.Enemy->StateTimer = action.AnticipationTime;
            filter.Enemy->Phase = EnemyActionPhase.Preparation;

            Log.Debug($"[Boss] {filter.Entity} staggered - forcing action slot {skillIndex + 1}");
        }

        private static int FindSkillSlot(EnemyDataAsset data, AssetRef<EnemyActionData> actionRef)
        {
            for (int i = 0; i < data.Actions.SkillActions.Count; i++)
            {
                if (data.Actions.SkillActions[i] == actionRef)
                    return i;
            }

            return -1;
        }

        // Orchestrates BossDataAsset.ComboChains (e.g. a triple-charge) - detects the exact
        // Recovery -> Chasing/Idle tick a chain-trigger action finished and, instead of letting
        // normal EnemyDecisionUtility.TrySelectAction scoring pick the next move, force-re-enters
        // Preparation on the SAME action (ForceComboHop below). Each hop therefore runs through the
        // real Preparation -> Telegraph -> Active -> Recovery cycle - a genuine telegraph and an
        // optional genuine retarget - with zero new Delivery/telegraph code, since it's just the
        // existing action executing again. Mirrors ForceBreakAction's own "write Phase/StateTimer
        // directly, bypass scoring" precedent, just re-running the SAME action instead of a
        // different one. Known accepted gap: if TickStagger's ForceBreakAction fires while a combo
        // still has hops remaining (both write Phase/CurrentActionSlot, TickStagger runs first),
        // the in-progress combo is silently abandoned rather than resumed - not exercised by any
        // boss yet, same "revisit if one needs it" acceptance ForceBreakAction's own OnInterrupted
        // gap already uses.
        private static void TickComboChain(Frame f, ref Filter filter, BossDataAsset bossData)
        {
            EnemyActionPhase previousPhase = filter.BossRuntimeState->LastObservedPhase;
            EnemyActionPhase currentPhase = filter.Enemy->Phase;
            filter.BossRuntimeState->LastObservedPhase = currentPhase; // unconditional, every tick

            if (bossData.ComboChains.Count == 0)
                return;

            bool justLeftRecovery = previousPhase == EnemyActionPhase.Recovery
                && (currentPhase == EnemyActionPhase.Chasing || currentPhase == EnemyActionPhase.Idle);

            if (justLeftRecovery == false)
                return;

            AssetRef<EnemyActionData> finishedAction = EnemyDecisionUtility.ResolveActionRef(bossData, filter.Enemy->CurrentActionSlot);

            if (finishedAction.IsValid == false || TryFindChain(bossData, finishedAction, out BossComboChainData chain) == false)
                return; // nothing just finished, or it isn't a chain-trigger action

            bool isContinuing = filter.BossRuntimeState->ActiveComboAction == finishedAction;

            if (isContinuing == true)
            {
                filter.BossRuntimeState->ComboRepeatsRemaining--;
            }
            else
            {
                filter.BossRuntimeState->ActiveComboAction = finishedAction;
                int remaining = chain.RepeatCount - 1;
                filter.BossRuntimeState->ComboRepeatsRemaining = (byte)(remaining < 0 ? 0 : remaining);
            }

            if (filter.BossRuntimeState->ComboRepeatsRemaining > 0)
            {
                ForceComboHop(f, ref filter, bossData, chain.RetargetEachRepeat);
                return;
            }

            // Last hop just finished.
            filter.BossRuntimeState->ActiveComboAction = default;

            if (chain.ExposedDurationOnFinish > FP._0)
                StatusEffectUtility.ApplyRupture(f, filter.Entity, chain.ExposedDurationOnFinish, chain.ExposedDamageMultiplierOnFinish);
        }

        private static bool TryFindChain(BossDataAsset bossData, AssetRef<EnemyActionData> actionRef, out BossComboChainData chain)
        {
            for (int i = 0; i < bossData.ComboChains.Count; i++)
            {
                if (bossData.ComboChains[i].TriggerAction == actionRef)
                {
                    chain = bossData.ComboChains[i];
                    return true;
                }
            }

            chain = default;
            return false;
        }

        // Re-triggers the same action that just finished, skipping normal TrySelectAction scoring
        // entirely - unlike ForceBreakAction (which resolves a DIFFERENT action's own slot), this
        // reuses Enemy.CurrentActionSlot as-is since it's the SAME action running again. Deliberately
        // does NOT touch Enemy.SkillTargetPosition/SkillStartPosition itself - same as
        // ForceBreakAction, entering Preparation is enough; the normal per-tick Preparation update
        // (EnemySystem/EnemyDeliveryData.OnAnticipating) re-derives aim from Enemy.Target live every
        // tick regardless of what those fields held before.
        private static void ForceComboHop(Frame f, ref Filter filter, BossDataAsset bossData, bool retarget)
        {
            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, bossData, filter.Enemy->CurrentActionSlot);

            if (action == null)
                return;

            if (retarget == true && bossData.AI.Targeting.IsValid == true)
            {
                EntityRef newTarget = f.FindAsset(bossData.AI.Targeting).SelectTarget(f, filter.Entity);

                if (newTarget != EntityRef.None)
                    filter.Enemy->Target = newTarget;
            }

            filter.Enemy->StateTimer = action.AnticipationTime;
            filter.Enemy->Phase = EnemyActionPhase.Preparation;

            Log.Debug($"[Boss] {filter.Entity} combo hop on slot {filter.Enemy->CurrentActionSlot}, {filter.BossRuntimeState->ComboRepeatsRemaining} repeat(s) left");
        }

        // Periodically re-resolves AI.Targeting mid-fight - EnemySystem only ever re-resolves
        // Enemy.Target on the rare Idle -> Chasing edge (EnemySystem.UpdateIdle/
        // ResolveInitialTarget), which a boss essentially never revisits once engaged (Enemy.Target
        // stays sticky through Chasing -> Preparation -> ... -> Recovery -> Chasing indefinitely) -
        // without this, a boss's first-ever target would stay locked for the whole fight, letting
        // that one player kite forever while the rest of the party free-fires. Gated to
        // Chasing/Recovery only - never mid-Preparation/Telegraph/Active, matching the existing "a
        // committed windup never retargets" philosophy the decoy-priority carve-out in
        // EnemySystem.UpdateChasing already follows.
        private static void TickRetarget(Frame f, ref Filter filter, BossDataAsset bossData)
        {
            if (bossData.RetargetInterval <= FP._0)
                return;

            if (filter.Enemy->Phase != EnemyActionPhase.Chasing && filter.Enemy->Phase != EnemyActionPhase.Recovery)
                return;

            filter.BossRuntimeState->RetargetTimer += f.DeltaTime;

            if (filter.BossRuntimeState->RetargetTimer < bossData.RetargetInterval)
                return;

            filter.BossRuntimeState->RetargetTimer = FP._0;

            if (bossData.AI.Targeting.IsValid == false)
                return;

            EntityRef newTarget = f.FindAsset(bossData.AI.Targeting).SelectTarget(f, filter.Entity);

            if (newTarget != EntityRef.None)
                filter.Enemy->Target = newTarget;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public BossRuntimeState* BossRuntimeState;
        }
    }
}
