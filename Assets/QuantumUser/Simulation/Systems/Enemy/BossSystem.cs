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
            for (int i = 0; i < data.SkillActions.Count; i++)
            {
                if (data.SkillActions[i] == actionRef)
                    return i;
            }

            return -1;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public BossRuntimeState* BossRuntimeState;
        }
    }
}
