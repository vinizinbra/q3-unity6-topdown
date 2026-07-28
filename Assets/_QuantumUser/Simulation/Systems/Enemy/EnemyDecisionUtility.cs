namespace Quantum
{
    using Photon.Deterministic;

    // Chooses which of an enemy's actions (BasicAction + SkillActions) should execute next, and
    // reads/writes each slot's cooldown - runs uniformly whether or not SkillActions is empty, so
    // there's only one selection code path to maintain. With just BasicAction to consider, this
    // deterministically reduces to "is it off cooldown and is the target in EngageRange", the same
    // outcome the original single-action gate produced.
    //
    // Score = SelectionWeight + RangeScore + TargetCountScore + PositionScore + PhaseScore -
    // RepetitionPenalty. PositionScore is a placeholder (always 0) until a line-of-sight/
    // positioning concept exists. PhaseScore only applies to a boss entity (BossDataAsset +
    // BossRuntimeState) - see the Boss layer.
    public static unsafe class EnemyDecisionUtility
    {
        private const int MaxSkillActions = 7;

        private static readonly FP RangeScoreWeight = 2;
        private static readonly FP TargetCountScoreWeight = 3;
        private static readonly FP PhaseScoreWeight = 2;
        private static readonly FP RepetitionPenalty = 4;

        // Slot 0 = BasicAction, 1..7 = SkillActions[0..6]. Returns false if nothing is eligible
        // (every action on cooldown, out of EngageRange, its Trigger condition unmet, or - for a
        // boss - not part of the current phase's pool).
        public static bool TrySelectAction(Frame f, EntityRef entity, Enemy* enemy, EnemyDataAsset data,
            FPVector3 targetPosition, FP sqrDistanceToTarget, out EnemyActionData bestAction, out int bestSlot)
        {
            bestAction = null;
            bestSlot = -1;
            FP bestScore = default;
            bool found = false;

            int previousSlot = enemy->CurrentActionSlot;
            int slotCount = 1 + System.Math.Min(data.Actions.SkillActions.Count, MaxSkillActions);

            BossDataAsset bossData = data as BossDataAsset;
            BossRuntimeState* boss = null;

            if (bossData != null)
            {
                f.Unsafe.TryGetPointer<BossRuntimeState>(entity, out boss);
            }

            for (int slot = 0; slot < slotCount; slot++)
            {
                if (boss != null && IsEligibleThisPhase(bossData, boss, slot) == false)
                    continue;

                EnemyActionData action = ResolveAction(f, data, slot);

                if (action == null)
                    continue;

                if (sqrDistanceToTarget > action.EngageRange * action.EngageRange)
                    continue;

                if (GetCooldownRemaining(f, entity, enemy, slot) > FP._0)
                    continue;

                if (IsTriggerConditionMet(f, entity, action.Trigger, sqrDistanceToTarget) == false)
                    continue;

                FP score = action.SelectionWeight;
                score += RangeScore(action.EngageRange, sqrDistanceToTarget);
                score += TargetCountScore(f, targetPosition, action.DamageRange);

                if (boss != null)
                {
                    score += PhaseScore(bossData, boss, slot);
                }

                if (slot == previousSlot)
                {
                    score -= RepetitionPenalty;
                }

                if (found == false || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestAction = action;
                    bestSlot = slot;
                }
            }

            return found;
        }

        // BasicAction (slot 0) is always eligible. A skill slot is eligible if it's either in
        // GlobalActionSlots (always available regardless of phase) or in CurrentPhaseIndex's own
        // ActionPoolSlots. No BossPhaseData configured yet (empty Phases, or CurrentPhaseIndex out
        // of range) doesn't restrict anything - an under-authored boss behaves like a plain
        // multi-action enemy rather than one where nothing is ever eligible.
        private static bool IsEligibleThisPhase(BossDataAsset bossData, BossRuntimeState* boss, int slot)
        {
            if (slot <= 0)
                return true;

            int skillIndex = slot - 1;

            if (bossData.GlobalActionSlots.Contains(skillIndex) == true)
                return true;

            if (boss->CurrentPhaseIndex < 0 || boss->CurrentPhaseIndex >= bossData.Phases.Count)
                return true;

            BossPhaseData currentPhase = bossData.Phases[boss->CurrentPhaseIndex];
            return currentPhase.ActionPoolSlots != null && currentPhase.ActionPoolSlots.Contains(skillIndex);
        }

        // Slightly prefers an action that's specifically part of the current phase's own pool over
        // one that's merely a GlobalActionSlots "always available" pick, so a boss favors its
        // phase-defining moves when both are eligible.
        private static FP PhaseScore(BossDataAsset bossData, BossRuntimeState* boss, int slot)
        {
            if (slot <= 0 || boss->CurrentPhaseIndex < 0 || boss->CurrentPhaseIndex >= bossData.Phases.Count)
                return FP._0;

            int skillIndex = slot - 1;
            BossPhaseData currentPhase = bossData.Phases[boss->CurrentPhaseIndex];

            return currentPhase.ActionPoolSlots != null && currentPhase.ActionPoolSlots.Contains(skillIndex)
                ? PhaseScoreWeight
                : FP._0;
        }

        // Resolves the EnemyActionData for a given slot - null if the slot is unset/out of range,
        // which callers treat as "not eligible" rather than an error (an authoring gap, e.g. an
        // enemy with SkillActions.Count < 7, is entirely normal).
        public static EnemyActionData ResolveAction(Frame f, EnemyDataAsset data, int slot)
        {
            if (slot <= 0)
            {
                return data.Actions.BasicAction.IsValid == true ? f.FindAsset(data.Actions.BasicAction) : null;
            }

            int skillIndex = slot - 1;

            if (skillIndex >= data.Actions.SkillActions.Count)
                return null;

            AssetRef<EnemyActionData> actionRef = data.Actions.SkillActions[skillIndex];
            return actionRef.IsValid == true ? f.FindAsset(actionRef) : null;
        }

        // BasicAction's cooldown lives directly on Enemy (every enemy has it, no extra component
        // needed); skill slots live on the optional EnemyActionSlots component (present only on
        // enemies that actually have SkillActions) - see that component's own comment for why.
        public static FP GetCooldownRemaining(Frame f, EntityRef entity, Enemy* enemy, int slot)
        {
            if (slot <= 0)
                return enemy->AttackCooldownRemaining;

            if (f.Unsafe.TryGetPointer<EnemyActionSlots>(entity, out var slots) == false)
                return FP._0;

            int skillIndex = slot - 1;
            return skillIndex < slots->SkillCooldowns.Length ? slots->SkillCooldowns[skillIndex] : FP._0;
        }

        public static void SetCooldownRemaining(Frame f, EntityRef entity, Enemy* enemy, int slot, FP value)
        {
            if (slot <= 0)
            {
                enemy->AttackCooldownRemaining = value;
                return;
            }

            if (f.Unsafe.TryGetPointer<EnemyActionSlots>(entity, out var slots) == false)
                return;

            int skillIndex = slot - 1;

            if (skillIndex < slots->SkillCooldowns.Length)
            {
                slots->SkillCooldowns[skillIndex] = value;
            }
        }

        // Cooldown is already checked separately (GetCooldownRemaining) - this only evaluates the
        // additional gate a non-Cooldown Trigger layers on top. OnDeath isn't evaluated here at all
        // - it fires once as the entity dies, a lifecycle moment this per-tick selection never
        // reaches; wiring that requires its own hook into the death pipeline (DamageUtility), not
        // this function, so it deliberately never qualifies here.
        private static bool IsTriggerConditionMet(Frame f, EntityRef entity, EnemyTriggerData trigger, FP sqrDistanceToTarget)
        {
            switch (trigger.Type)
            {
                case EnemyTriggerType.OnProximity:
                    return sqrDistanceToTarget <= trigger.Radius * trigger.Radius;

                case EnemyTriggerType.OnHealthThreshold:
                    if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false || health->MaxHealth <= FP._0)
                        return false;

                    return health->CurrentHealth / health->MaxHealth <= trigger.HealthPercent;

                case EnemyTriggerType.OnDeath:
                    return false;

                default: // Cooldown - no additional gate beyond the cooldown check itself.
                    return true;
            }
        }

        // Rewards being well inside EngageRange over just barely qualifying - 1 at zero distance,
        // 0 at the edge of EngageRange.
        private static FP RangeScore(FP engageRange, FP sqrDistance)
        {
            if (engageRange <= FP._0)
                return FP._0;

            FP distance = FPMath.Sqrt(sqrDistance);
            FP normalized = FPMath.Clamp01(FP._1 - distance / engageRange);
            return normalized * RangeScoreWeight;
        }

        // Rewards an action whose connect DamageRange would catch more than just the primary target -
        // relevant for area deliveries (Leap, a future GroundArea/Aura), neutral for single-target
        // ones (which simply won't have anyone else this close).
        private static FP TargetCountScore(Frame f, FPVector3 targetPosition, FP range)
        {
            if (range <= FP._0)
                return FP._0;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, targetPosition, range);
            int extraTargets = hits.Count > 0 ? hits.Count - 1 : 0;
            return extraTargets * TargetCountScoreWeight;
        }
    }
}
