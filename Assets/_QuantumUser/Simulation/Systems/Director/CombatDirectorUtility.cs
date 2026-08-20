namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Domain 2 (Combat Director) - the full 10-step pulse algorithm from the Survival Director
    // design: read phase, add budget, measure pressure, stop if target already met, find/select/
    // spawn an authored group, subtract its cost, repeat until the purchase limit or no valid
    // group/anchor remains. Never spawns an individual enemy - only ever purchases a whole
    // EnemyGroupConfig, so encounter design lives entirely in that asset.
    public static unsafe class CombatDirectorUtility
    {
        public static void TryPulse(Frame f, SurvivalPhase phase, DirectorConfig directorConfig, LifecycleConfig lifecycleConfig, BalanceConfig balance)
        {
            f.Global->DirectorPulseTimer -= f.DeltaTime;

            if (f.Global->DirectorPulseTimer > FP._0)
                return;

            f.Global->DirectorPulseTimer = phase.PulseInterval;
            f.Global->DirectorBudget += phase.BudgetPerPulse * ResolveBudgetMultiplier(f);

            // One "front" per player cluster - cohesive parties resolve to a single front at the
            // team centroid, so this is identical to the pre-cluster Director for solo/grouped play.
            // See PlayerClusterDirectorUtility.
            if (PlayerClusterDirectorUtility.BuildAnchors(f, phase, directorConfig, balance, out var plan) == false)
            {
                Log.Debug("[Director] pulse skipped - no players to spawn combat around");
                return;
            }

            // Splitting legitimately needs more concurrent enemies (multiple fronts), so the alive
            // cap scales by the same multiplier the budget/pressure do - but never below the
            // authored value.
            FP splitThreat = f.Global->SplitThreatMultiplier <= FP._0 ? FP._1 : f.Global->SplitThreatMultiplier;
            int maxAlive = FPMath.RoundToInt(phase.MaxAliveEnemies * splitThreat);
            if (maxAlive < phase.MaxAliveEnemies)
                maxAlive = phase.MaxAliveEnemies;

            // A front that can't place a group (no valid anchor near it) shouldn't starve the
            // others - it's marked exhausted and the loop moves on to the next neediest.
            Span<bool> exhausted = stackalloc bool[PlayerClusterDirectorUtility.MaxPlayers];
            for (int i = 0; i < plan.Count; i++)
                exhausted[i] = false;

            int purchases = 0;
            int maxPurchases = directorConfig.MaxPurchasesPerPulse * plan.Count;

            while (purchases < maxPurchases)
            {
                // Serve the front furthest below its own pressure target (per-front, not one global
                // gate - that's the whole point: a busy front near one player must not stop a lonely
                // teammate's front from filling).
                int front = SelectNeediestFront(f, plan, directorConfig, exhausted);

                if (front < 0)
                    break; // every front is at/above target, or exhausted

                int aliveCount = CountAliveDirectorEnemies(f);

                if (TrySelectGroup(f, phase, aliveCount, maxAlive, out EnemyGroupConfig group, out AssetRef<EnemyGroupConfig> groupRef, out FP groupCost) == false)
                {
                    Log.Debug($"[Director] pulse stopped - no valid group (budget={f.Global->DirectorBudget}, alive={aliveCount}, cap={maxAlive})");
                    break;
                }

                // Major enemies (Elite+) stay a GLOBAL event - never duplicated per front. They
                // anchor at the party centroid regardless of which front's deficit triggered the
                // purchase (the Boss itself uses a separate path, RunPhaseUtility.BeginBossEncounter).
                bool major = GroupContainsMajor(f, group);
                FPVector3 anchor = major ? plan.GlobalCentroid : plan.Centers[front];

                if (GroupSpawnerUtility.TrySpawnGroup(f, group, groupRef, anchor, directorConfig, out int spawnedCount) == false)
                {
                    Log.Debug($"[Director] {group.name} found no valid spawn at front {front} - marking it exhausted this pulse");
                    exhausted[front] = true;
                    continue;
                }

                f.Global->DirectorBudget -= groupCost;
                purchases++;

                Log.Debug($"[Director] purchased {group.name} ({spawnedCount} enemies){(major ? " [major-global]" : $" at front {front}")} for {groupCost} - budget now {f.Global->DirectorBudget}, alive was {aliveCount}/{maxAlive}, split x{splitThreat}");
            }
        }

        // Largest (TargetPressure - LocalPressure) among non-exhausted fronts, and only if that
        // deficit is positive (front still wants more). -1 if every front is satisfied/exhausted.
        private static int SelectNeediestFront(Frame f, PlayerClusterDirectorUtility.AnchorPlan plan, DirectorConfig directorConfig, Span<bool> exhausted)
        {
            int best = -1;
            FP bestDeficit = FP._0;

            for (int i = 0; i < plan.Count; i++)
            {
                if (exhausted[i])
                    continue;

                // One front = the cohesive/solo case: use the exact pre-cluster global pressure gate
                // (all active enemies) rather than a radius-limited count, so solo/grouped play is
                // unchanged. Only a genuine 2+ front split scopes pressure to each front's own area.
                FP frontPressure = plan.Count == 1
                    ? PlayerClusterDirectorUtility.GlobalPressure(f)
                    : PlayerClusterDirectorUtility.LocalPressure(f, plan.Centers[i], directorConfig.ClusterPressureRadius);
                FP deficit = plan.TargetPressure[i] - frontPressure;

                if (deficit > bestDeficit)
                {
                    bestDeficit = deficit;
                    best = i;
                }
            }

            return best;
        }

        // True if any member is Elite tier or higher - such a group is spawned once at the global
        // centroid, never round-robined per cluster (Elite/midboss stay a global event).
        private static bool GroupContainsMajor(Frame f, EnemyGroupConfig group)
        {
            if (group.Members == null)
                return false;

            foreach (GroupMemberEntry member in group.Members)
            {
                if (member.EnemyData.Id.IsValid == false)
                    continue;

                EnemyDataAsset data = f.FindAsset(member.EnemyData);
                if (data != null && data.Tier >= EnemyTier.Elite)
                    return true;
            }

            return false;
        }

        // Run curve (ramps over the 12-minute run) * co-op multiplier (scales with live player
        // count) - see BalanceConfig.CurveChannel.DirectorBudget/CoopGlobalKey.DirectorBudget,
        // reserved for exactly this: docs/survival-director.md's "Milestone 7 (Co-op Scaling)".
        // Missing BalanceConfig is a graceful no-op (1x) rather than halting the Director, same
        // precedent as EnemyBalanceUtility.ResolveEnemyStats - BudgetPerPulse alone still applies.
        private static FP ResolveBudgetMultiplier(Frame f)
        {
            BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);

            if (balance == null)
            {
                Log.Error("[Director] RuntimeConfig.BalanceConfig did not resolve - DirectorBudget accumulating at its authored BudgetPerPulse only (1x), no run-curve/co-op scaling applied. Assign it on RuntimeConfig.");
                return FP._1;
            }

            FP curveMultiplier = balance.Evaluate(CurveChannel.DirectorBudget, f.Global->SurvivalTime);
            FP coopMultiplier = balance.GetCoopGlobal(CoopGlobalKey.DirectorBudget, f.PlayerCount);

            // Player-cluster split scaling (>=1, 1 when cohesive - see PlayerClusterDirectorUtility).
            // Since the base already applies coop(f.PlayerCount) and the split multiplier is
            // FinalTotal/coop(liveClusterCount), the two compose into the summed-cluster budget the
            // design specifies, cap included. Defended to 1 for the pre-first-tick zero default.
            FP splitMultiplier = f.Global->SplitThreatMultiplier <= FP._0 ? FP._1 : f.Global->SplitThreatMultiplier;

            return curveMultiplier * coopMultiplier * splitMultiplier;
        }

        // Refunds a fraction of the enemy's own cost into DirectorBudget, then destroys it. Called
        // by EnemyLifecycleSystem's own natural Irrelevant->Retired timeout - "this Director-
        // purchased enemy is going away without being killed" (see docs/survival-director.md).
        // Breathing deliberately does NOT force-retire enemies of its own (see docs/run-phase.md) -
        // it just stops spawning more and lets this same natural timeout (or a real player kill)
        // clear whatever's left, which is also what SurvivalProgressionUtility.IsEncounterCleared
        // waits on before the Break's own countdown starts.
        public static void RetireEnemy(Frame f, EntityRef entity, EnemyDataAsset data, LifecycleConfig lifecycleConfig)
        {
            FP refund = data.ResolveCost(f) * lifecycleConfig.RefundFraction;
            f.Global->DirectorBudget += refund;

            Log.Debug($"[Director] retiring {entity} ({data.name}) - refunding {refund}, DirectorBudget now {f.Global->DirectorBudget}");

            f.Destroy(entity);
        }

        // Every entity with EnemyLifecycle is, by construction, a Director purchase - Retired
        // entities are destroyed the same tick they reach that state (see EnemyLifecycleSystem),
        // so anything still around counts as alive.
        private static int CountAliveDirectorEnemies(Frame f)
        {
            int count = 0;
            var filtered = f.Filter<EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out EnemyLifecycle _))
            {
                count++;
            }

            return count;
        }

        private static int CountAliveForGroup(Frame f, AssetRef<EnemyGroupConfig> groupRef)
        {
            int count = 0;
            var filtered = f.Filter<EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out EnemyLifecycle lifecycle))
            {
                if (lifecycle.SourceGroup == groupRef)
                    count++;
            }

            return count;
        }

        // Deterministic weighted roll among qualifying candidates, in authored (phase.AllowedGroups)
        // order - a single f.RNG->Next(0, totalWeight) draw walked against each candidate's own
        // Weight, not one roll per candidate, so the result only ever depends on the RNG state and
        // the candidate list itself. Variety is still meant to come from authoring more groups, not
        // from a tactical scoring formula - Weight only biases how often an already-valid group is
        // picked relative to its siblings.
        private static bool TrySelectGroup(Frame f, SurvivalPhase phase, int aliveCount, int maxAliveEnemies, out EnemyGroupConfig chosen, out AssetRef<EnemyGroupConfig> chosenRef, out FP chosenCost)
        {
            List<AssetRef<EnemyGroupConfig>> validGroups = new List<AssetRef<EnemyGroupConfig>>();
            List<FP> validCosts = new List<FP>();
            List<FP> validWeights = new List<FP>();
            FP totalWeight = FP._0;

            if (phase.AllowedGroups == null || phase.AllowedGroups.Count == 0)
            {
                Log.Debug("[Director] current phase has no AllowedGroups authored");
            }
            else
            {
                for (int i = 0; i < phase.AllowedGroups.Count; i++)
                {
                    AssetRef<EnemyGroupConfig> groupRef = phase.AllowedGroups[i];

                    if (groupRef.Id.IsValid == false)
                    {
                        Log.Debug($"[Director] AllowedGroups[{i}] rejected - AssetRef not assigned");
                        continue;
                    }

                    EnemyGroupConfig candidate = f.FindAsset(groupRef);

                    if (candidate == null)
                    {
                        Log.Error($"[Director] AllowedGroups[{i}] ({groupRef}) rejected - did not resolve to an asset (dangling reference)");
                        continue;
                    }

                    if (candidate.Weight <= FP._0)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - Weight <= 0 (soft-disabled)");
                        continue; // soft-disabled
                    }

                    if (f.Global->SurvivalTime < candidate.MinimumSurvivalTime)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - not unlocked yet ({f.Global->SurvivalTime} < MinimumSurvivalTime {candidate.MinimumSurvivalTime})");
                        continue; // not unlocked yet
                    }

                    if (candidate.MaximumSurvivalTime > FP._0 && f.Global->SurvivalTime > candidate.MaximumSurvivalTime)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - unlock window passed ({f.Global->SurvivalTime} > MaximumSurvivalTime {candidate.MaximumSurvivalTime})");
                        continue; // unlock window already passed
                    }

                    FP cost = candidate.ComputeCost(f);

                    if (cost > f.Global->DirectorBudget)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - not affordable (cost {cost} > budget {f.Global->DirectorBudget})");
                        continue; // not affordable
                    }

                    if (aliveCount + candidate.ComputeMemberCount() > maxAliveEnemies)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - would exceed alive cap ({aliveCount} + {candidate.ComputeMemberCount()} > {maxAliveEnemies})");
                        continue; // would exceed the (split-scaled) alive cap
                    }

                    if (candidate.MaxConcurrent > 0 && CountAliveForGroup(f, groupRef) >= candidate.MaxConcurrent)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - MaxConcurrent {candidate.MaxConcurrent} already reached");
                        continue; // concurrent copies already at MaxConcurrent
                    }

                    validGroups.Add(groupRef);
                    validCosts.Add(cost);
                    validWeights.Add(candidate.Weight);
                    totalWeight += candidate.Weight;
                }
            }

            if (validGroups.Count == 0)
            {
                chosen = null;
                chosenRef = default;
                chosenCost = FP._0;
                return false;
            }

            FP roll = f.RNG->Next(FP._0, totalWeight);
            FP cumulative = FP._0;
            int chosenIndex = validGroups.Count - 1; // falls back to the last candidate if float rounding leaves `roll` a hair under totalWeight

            for (int i = 0; i < validGroups.Count; i++)
            {
                cumulative += validWeights[i];

                if (roll < cumulative)
                {
                    chosenIndex = i;
                    break;
                }
            }

            chosenRef = validGroups[chosenIndex];
            chosen = f.FindAsset(chosenRef);
            chosenCost = validCosts[chosenIndex];
            return true;
        }
    }
}
