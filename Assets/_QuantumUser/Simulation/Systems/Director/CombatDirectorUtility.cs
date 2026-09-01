namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Domain 2 (Combat Director) - the full 10-step pulse algorithm from the Survival Director
    // design: read phase, add budget, measure pressure, stop if target already met, find/select/
    // spawn an authored group OR a single directly-authored enemy, subtract its cost, repeat until
    // the purchase limit or no valid candidate/anchor remains. AllowedGroups and AllowedEnemies
    // share one weighted-draw candidate pool (TrySelectSpawn) so a phase can freely mix whole
    // encounters and lone spawns within the same pulse.
    public static unsafe class CombatDirectorUtility
    {
        // One purchase's worth of "what to spawn next" - either a whole EnemyGroupConfig or a
        // single AllowedEnemies entry, picked by the same weighted roll (see TrySelectSpawn). Kept
        // as a small carrier struct rather than two parallel out-params so the purchase loop in
        // TryPulse has one thing to branch on.
        private struct SpawnCandidate
        {
            public bool IsGroup;
            public EnemyGroupConfig Group;
            public AssetRef<EnemyGroupConfig> GroupRef;
            public EnemySpawnEntry Enemy;
            public FP Cost;
        }

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
                Log.Error("[Director] pulse skipped - no players to spawn combat around");
                return;
            }

            // Splitting legitimately needs more concurrent enemies (multiple fronts), so the alive
            // cap scales by the same multiplier the budget/pressure do - as does any run-wide
            // density modifier (Overpopulation/Elite Territory/Escalation, see
            // EncounterModifierUtility). Never below the authored value when only splitting is at
            // play; a density modifier that genuinely LOWERS density (Elite Territory) is allowed
            // to take it under, since that's the whole point of the trade.
            FP splitThreat = f.Global->SplitThreatMultiplier <= FP._0 ? FP._1 : f.Global->SplitThreatMultiplier;
            FP density = EncounterModifierUtility.ResolveSpawnDensityMultiplier(f);
            int maxAlive = FPMath.RoundToInt(phase.MaxAliveEnemies * splitThreat * density);
            int splitFloor = FPMath.RoundToInt(phase.MaxAliveEnemies * density);
            if (maxAlive < splitFloor)
                maxAlive = splitFloor;
            if (maxAlive < 1)
                maxAlive = 1;

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

                if (TrySelectSpawn(f, phase, aliveCount, maxAlive, out SpawnCandidate candidate) == false)
                {
                    Log.Error($"[Director] pulse stopped - no valid group or enemy (budget={f.Global->DirectorBudget}, alive={aliveCount}, cap={maxAlive})");
                    break;
                }

                // Major enemies (Elite+) stay a GLOBAL event - never duplicated per front. They
                // anchor at the party centroid regardless of which front's deficit triggered the
                // purchase (the Boss itself uses a separate path, RunPhaseUtility.BeginBossEncounter).
                bool major = candidate.IsGroup ? GroupContainsMajor(f, candidate.Group) : EnemyIsMajor(f, candidate.Enemy.EnemyData);
                FPVector3 anchor = major ? plan.GlobalCentroid : plan.Centers[front];

                bool spawnedOk;
                int spawnedCount;
                string candidateName;

                if (candidate.IsGroup)
                {
                    spawnedOk = GroupSpawnerUtility.TrySpawnGroup(f, candidate.Group, candidate.GroupRef, anchor, major, directorConfig, out spawnedCount);
                    candidateName = candidate.Group.name;
                }
                else
                {
                    spawnedOk = GroupSpawnerUtility.TrySpawnEnemy(f, candidate.Enemy, anchor, major, directorConfig);
                    spawnedCount = spawnedOk ? 1 : 0;
                    candidateName = f.FindAsset(candidate.Enemy.EnemyData)?.name ?? candidate.Enemy.EnemyData.ToString();
                }

                if (spawnedOk == false)
                {
                    Log.Error($"[Director] {candidateName} found no valid spawn at front {front} - marking it exhausted this pulse");
                    exhausted[front] = true;
                    continue;
                }

                f.Global->DirectorBudget -= candidate.Cost;
                purchases++;

                Log.Error($"[Director] purchased {candidateName} ({spawnedCount} enemies){(major ? " [major-global]" : $" at front {front}")} for {candidate.Cost} - budget now {f.Global->DirectorBudget}, alive was {aliveCount}/{maxAlive}, split x{splitThreat}");
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
        // centroid, never round-robined per cluster (Elite/midboss stay a global event). Also the
        // "is this an Elite encounter" test EncounterModifierUtility.ResolveGroupWeightMultiplier
        // biases its roll by (Elite Territory), which is why this is internal rather than private -
        // one definition of "major group", not two that could drift.
        internal static bool GroupContainsMajor(Frame f, EnemyGroupConfig group)
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

        // AllowedEnemies' counterpart to GroupContainsMajor - a direct single-enemy candidate is
        // "major" purely by its own Tier, same Elite+ threshold.
        internal static bool EnemyIsMajor(Frame f, AssetRef<EnemyDataAsset> enemyDataRef)
        {
            if (enemyDataRef.Id.IsValid == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemyDataRef);
            return data != null && data.Tier >= EnemyTier.Elite;
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

            // Run-wide encounter modifiers (Overpopulation/Elite Territory) plus Escalation's
            // within-phase ramp - exactly 1 for a run where none was picked. See
            // EncounterModifierUtility.
            FP densityMultiplier = EncounterModifierUtility.ResolveSpawnDensityMultiplier(f);

            return curveMultiplier * coopMultiplier * splitMultiplier * densityMultiplier;
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

            Log.Error($"[Director] retiring {entity} ({data.name}) - refunding {refund}, DirectorBudget now {f.Global->DirectorBudget}");

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

        // AllowedEnemies' counterpart to CountAliveForGroup - counts by Enemy.EnemyData across
        // every EnemyLifecycle-carrying entity regardless of how it was spawned (group or direct),
        // since a direct spawn has no owning group to key a count off.
        private static int CountAliveForEnemy(Frame f, AssetRef<EnemyDataAsset> enemyDataRef)
        {
            int count = 0;
            var filtered = f.Filter<EnemyLifecycle, Enemy>();

            while (filtered.Next(out EntityRef entity, out EnemyLifecycle _, out Enemy enemy))
            {
                if (enemy.EnemyData == enemyDataRef)
                    count++;
            }

            return count;
        }

        // Deterministic weighted roll among qualifying candidates from BOTH phase.AllowedGroups and
        // phase.AllowedEnemies, in authored order (groups first, then enemies) - a single
        // f.RNG->Next(0, totalWeight) draw walked against each candidate's own Weight, not one roll
        // per candidate, so the result only ever depends on the RNG state and the candidate list
        // itself. Variety is still meant to come from authoring more groups/enemies, not from a
        // tactical scoring formula - Weight only biases how often an already-valid candidate is
        // picked relative to its siblings.
        private static bool TrySelectSpawn(Frame f, SurvivalPhase phase, int aliveCount, int maxAliveEnemies, out SpawnCandidate chosen)
        {
            List<SpawnCandidate> valid = new List<SpawnCandidate>();
            List<FP> validWeights = new List<FP>();
            FP totalWeight = FP._0;

            if (phase.AllowedGroups == null || phase.AllowedGroups.Count == 0)
            {
                Log.Error("[Director] current phase has no AllowedGroups authored");
            }
            else
            {
                for (int i = 0; i < phase.AllowedGroups.Count; i++)
                {
                    AssetRef<EnemyGroupConfig> groupRef = phase.AllowedGroups[i];

                    if (groupRef.Id.IsValid == false)
                    {
                        Log.Error($"[Director] AllowedGroups[{i}] rejected - AssetRef not assigned");
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
                        Log.Error($"[Director] {candidate.name} rejected - Weight <= 0 (soft-disabled)");
                        continue; // soft-disabled
                    }

                    if (f.Global->SurvivalTime < candidate.MinimumSurvivalTime)
                    {
                        Log.Error($"[Director] {candidate.name} rejected - not unlocked yet ({f.Global->SurvivalTime} < MinimumSurvivalTime {candidate.MinimumSurvivalTime})");
                        continue; // not unlocked yet
                    }

                    if (candidate.MaximumSurvivalTime > FP._0 && f.Global->SurvivalTime > candidate.MaximumSurvivalTime)
                    {
                        Log.Error($"[Director] {candidate.name} rejected - unlock window passed ({f.Global->SurvivalTime} > MaximumSurvivalTime {candidate.MaximumSurvivalTime})");
                        continue; // unlock window already passed
                    }

                    FP cost = candidate.ComputeCost(f);

                    if (cost > f.Global->DirectorBudget)
                    {
                        Log.Error($"[Director] {candidate.name} rejected - not affordable (cost {cost} > budget {f.Global->DirectorBudget})");
                        continue; // not affordable
                    }

                    if (aliveCount + candidate.ComputeMemberCount() > maxAliveEnemies)
                    {
                        Log.Error($"[Director] {candidate.name} rejected - would exceed alive cap ({aliveCount} + {candidate.ComputeMemberCount()} > {maxAliveEnemies})");
                        continue; // would exceed the (split-scaled) alive cap
                    }

                    if (candidate.MaxConcurrent > 0 && CountAliveForGroup(f, groupRef) >= candidate.MaxConcurrent)
                    {
                        Log.Error($"[Director] {candidate.name} rejected - MaxConcurrent {candidate.MaxConcurrent} already reached");
                        continue; // concurrent copies already at MaxConcurrent
                    }

                    // Run-wide weighting bias (Elite Territory makes Elite-bearing groups far more
                    // likely) - exactly 1 for every group when nothing is modifying the run, so the
                    // roll below is unchanged in the normal case. See EncounterModifierUtility.
                    FP weight = candidate.Weight * EncounterModifierUtility.ResolveGroupWeightMultiplier(f, candidate);

                    valid.Add(new SpawnCandidate { IsGroup = true, Group = candidate, GroupRef = groupRef, Cost = cost });
                    validWeights.Add(weight);
                    totalWeight += weight;
                }
            }

            if (phase.AllowedEnemies != null)
            {
                for (int i = 0; i < phase.AllowedEnemies.Length; i++)
                {
                    EnemySpawnEntry entry = phase.AllowedEnemies[i];

                    if (entry.EnemyData.Id.IsValid == false)
                    {
                        Log.Error($"[Director] AllowedEnemies[{i}] rejected - EnemyData not assigned");
                        continue;
                    }

                    if (entry.Weight <= FP._0)
                    {
                        Log.Error($"[Director] AllowedEnemies[{i}] rejected - Weight <= 0 (soft-disabled)");
                        continue; // soft-disabled
                    }

                    EnemyDataAsset data = f.FindAsset(entry.EnemyData);

                    if (data == null)
                    {
                        Log.Error($"[Director] AllowedEnemies[{i}] ({entry.EnemyData}) rejected - did not resolve to an asset (dangling reference)");
                        continue;
                    }

                    if (f.Global->SurvivalTime < entry.MinimumSurvivalTime)
                    {
                        Log.Error($"[Director] {data.name} (direct) rejected - not unlocked yet ({f.Global->SurvivalTime} < MinimumSurvivalTime {entry.MinimumSurvivalTime})");
                        continue; // not unlocked yet
                    }

                    if (entry.MaximumSurvivalTime > FP._0 && f.Global->SurvivalTime > entry.MaximumSurvivalTime)
                    {
                        Log.Error($"[Director] {data.name} (direct) rejected - unlock window passed ({f.Global->SurvivalTime} > MaximumSurvivalTime {entry.MaximumSurvivalTime})");
                        continue; // unlock window already passed
                    }

                    FP cost = data.ResolveCost(f);

                    if (cost > f.Global->DirectorBudget)
                    {
                        Log.Error($"[Director] {data.name} (direct) rejected - not affordable (cost {cost} > budget {f.Global->DirectorBudget})");
                        continue; // not affordable
                    }

                    if (aliveCount + 1 > maxAliveEnemies)
                    {
                        Log.Error($"[Director] {data.name} (direct) rejected - would exceed alive cap ({aliveCount} + 1 > {maxAliveEnemies})");
                        continue; // would exceed the (split-scaled) alive cap
                    }

                    if (entry.MaxConcurrent > 0 && CountAliveForEnemy(f, entry.EnemyData) >= entry.MaxConcurrent)
                    {
                        Log.Error($"[Director] {data.name} (direct) rejected - MaxConcurrent {entry.MaxConcurrent} already reached");
                        continue; // concurrent copies already at MaxConcurrent
                    }

                    FP weight = entry.Weight * EncounterModifierUtility.ResolveEnemyWeightMultiplier(f, data);

                    valid.Add(new SpawnCandidate { IsGroup = false, Enemy = entry, Cost = cost });
                    validWeights.Add(weight);
                    totalWeight += weight;
                }
            }

            if (valid.Count == 0)
            {
                chosen = default;
                return false;
            }

            FP roll = f.RNG->Next(FP._0, totalWeight);
            FP cumulative = FP._0;
            int chosenIndex = valid.Count - 1; // falls back to the last candidate if float rounding leaves `roll` a hair under totalWeight

            for (int i = 0; i < valid.Count; i++)
            {
                cumulative += validWeights[i];

                if (roll < cumulative)
                {
                    chosenIndex = i;
                    break;
                }
            }

            chosen = valid[chosenIndex];
            return true;
        }
    }
}
