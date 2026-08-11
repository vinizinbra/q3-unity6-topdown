namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Domain 2 (Combat Director) - the full 10-step pulse algorithm from the Survival Director
    // design: read phase, add budget, measure pressure, stop if target already met, find/select/
    // spawn an authored group, subtract its cost, repeat until the purchase limit or no valid
    // group/anchor remains. Never spawns an individual enemy - only ever purchases a whole
    // EnemyGroupConfig, so encounter design lives entirely in that asset.
    public static unsafe class CombatDirectorUtility
    {
        public static void TryPulse(Frame f, SurvivalPhase phase, DirectorConfig directorConfig, LifecycleConfig lifecycleConfig)
        {
            f.Global->DirectorPulseTimer -= f.DeltaTime;

            if (f.Global->DirectorPulseTimer > FP._0)
                return;

            f.Global->DirectorPulseTimer = phase.PulseInterval;
            f.Global->DirectorBudget += phase.BudgetPerPulse * ResolveBudgetMultiplier(f);

            if (TryComputePredictedCombatCenter(f, directorConfig, out FPVector3 predictedCombatCenter) == false)
            {
                Log.Debug("[Director] pulse skipped - no players to spawn combat around");
                return;
            }

            int purchases = 0;

            while (purchases < directorConfig.MaxPurchasesPerPulse)
            {
                FP pressure = ComputeRelevantPressure(f);

                if (pressure >= phase.TargetPressure)
                {
                    Log.Debug($"[Director] pulse stopped - pressure {pressure} already at/above target {phase.TargetPressure}");
                    break;
                }

                int aliveCount = CountAliveDirectorEnemies(f);

                if (TrySelectGroup(f, phase, aliveCount, out EnemyGroupConfig group, out AssetRef<EnemyGroupConfig> groupRef, out FP groupCost) == false)
                {
                    Log.Debug($"[Director] pulse stopped - no valid group (budget={f.Global->DirectorBudget}, alive={aliveCount}, cap={phase.MaxAliveEnemies})");
                    break;
                }

                // GroupSpawnerUtility owns WHERE entirely (ring anchor + per-member placement,
                // bounded by DirectorConfig.MaxGroupSpawnAttempts) - budget is only ever spent
                // below, after it reports a full success, never before (see its own "Budget
                // Transaction" comment). A failure here just means the next pulse, a few seconds
                // later, tries again with a fresh prediction - not an unbounded retry loop.
                if (GroupSpawnerUtility.TrySpawnGroup(f, group, groupRef, predictedCombatCenter, directorConfig, out int spawnedCount) == false)
                {
                    Log.Debug($"[Director] pulse stopped - {group.name} found no valid spawn near the predicted combat center");
                    break;
                }

                f.Global->DirectorBudget -= groupCost;
                purchases++;

                Log.Debug($"[Director] purchased {group.name} ({spawnedCount} enemies) for {groupCost} - budget now {f.Global->DirectorBudget}, pressure was {pressure}/{phase.TargetPressure}, alive was {aliveCount}/{phase.MaxAliveEnemies}");
            }
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

            return curveMultiplier * coopMultiplier;
        }

        private static FP ComputeRelevantPressure(Frame f)
        {
            FP pressure = FP._0;
            var filtered = f.Filter<Enemy, EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out Enemy enemy, out EnemyLifecycle lifecycle))
            {
                // State == Active is exactly "currently relevant" (EnemyLifecycleSystem only ever
                // sets it that way) - no separate relevance recheck needed here.
                if (lifecycle.State != EnemyLifecycleState.Active)
                    continue;

                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);
                pressure += data.ResolveCost(f);
            }

            return pressure;
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
        private static bool TrySelectGroup(Frame f, SurvivalPhase phase, int aliveCount, out EnemyGroupConfig chosen, out AssetRef<EnemyGroupConfig> chosenRef, out FP chosenCost)
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

                    if (aliveCount + candidate.ComputeMemberCount() > phase.MaxAliveEnemies)
                    {
                        Log.Debug($"[Director] {candidate.name} rejected - would exceed alive cap ({aliveCount} + {candidate.ComputeMemberCount()} > {phase.MaxAliveEnemies})");
                        continue; // would exceed the alive cap
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

        // TeamCenter + AverageVelocity * PredictionTime - the "moving combat bubble". False if no
        // players exist yet to spawn combat around.
        private static bool TryComputePredictedCombatCenter(Frame f, DirectorConfig directorConfig, out FPVector3 predictedCombatCenter)
        {
            FPVector3 positionSum = FPVector3.Zero;
            FPVector3 velocitySum = FPVector3.Zero;
            int count = 0;

            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                    continue;

                positionSum += transform->Position;
                count++;

                if (f.Unsafe.TryGetPointer<KCC>(entity, out var kcc) == true)
                {
                    velocitySum += kcc->Data.RealVelocity;
                }
            }

            if (count == 0)
            {
                predictedCombatCenter = default;
                return false;
            }

            FPVector3 teamCenter = positionSum / (FP)count;
            FPVector3 averageVelocity = velocitySum / (FP)count;
            predictedCombatCenter = teamCenter + averageVelocity * directorConfig.PredictionTime;
            return true;
        }
    }
}
