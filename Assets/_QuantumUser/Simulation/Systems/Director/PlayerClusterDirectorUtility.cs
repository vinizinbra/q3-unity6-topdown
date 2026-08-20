namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Co-op "player cluster" spawning for the Survival Director (see docs/survival-director.md's
    // "Player clusters" section). When players wander apart, spawning everything at the party
    // centroid drops enemies in the empty gap between them, where they instantly fall outside
    // LifecycleConfig.RelevantRange of anyone and retire without ever engaging. Instead, each
    // proximity cluster becomes its own combat front.
    //
    // Threat is driven entirely by the EXISTING per-player-count curve
    // (BalanceConfig.GetCoopGlobal(DirectorBudget, n)) reused verbatim as "GetThreatBudget(n)": a
    // cluster of n players requests exactly what a normal n-player party would, summed across
    // clusters and capped by DirectorConfig.MaxSplitThreatMultiplier. Nothing here is hardcoded to
    // 4 players - every formula reads a live cluster size. All working sets are stackalloc'd, so the
    // per-tick scalar update never touches the heap; only the per-pulse anchor plan allocates.
    public static unsafe class PlayerClusterDirectorUtility
    {
        // Quantum.Input.MAX_COUNT.
        public const int MaxPlayers = 4;

        // This pulse's spawn fronts. Cohesive (or <=1 player) is Count==1 at the party centroid,
        // byte-identical to the pre-cluster Director.
        public struct AnchorPlan
        {
            public int Count;                 // spawn fronts this pulse
            public FPVector3[] Centers;       // predicted center per front
            public FP[] TargetPressure;       // per-front pressure target (sums to phase.TargetPressure * SplitThreatMultiplier)
            public FPVector3 GlobalCentroid;  // where major (Elite+) groups anchor, never per-front
        }

        // Fills flat position + velocity for every live player entity; returns the count.
        public static int GatherPlayers(Frame f, Span<FPVector3> pos, Span<FPVector3> vel)
        {
            int count = 0;
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (count >= MaxPlayers)
                    break;

                if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                    continue;

                pos[count] = transform->Position;
                vel[count] = f.Unsafe.TryGetPointer<KCC>(entity, out var kcc) ? kcc->Data.RealVelocity : FPVector3.Zero;
                count++;
            }

            return count;
        }

        // Union-find by flat XZ distance <= clusterDistance. Writes a compacted 0-based cluster id
        // per player, returns the cluster count. Deterministic: fixed iteration order + a
        // lowest-index-root union tie-break.
        public static int Cluster(Span<FPVector3> pos, int count, FP clusterDistance, Span<int> clusterId)
        {
            Span<int> parent = stackalloc int[MaxPlayers];
            for (int i = 0; i < count; i++)
                parent[i] = i;

            FP thresholdSqr = clusterDistance * clusterDistance;

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (FlatSqrDistance(pos[i], pos[j]) <= thresholdSqr)
                        Union(parent, i, j);

            Span<int> rootToId = stackalloc int[MaxPlayers];
            for (int i = 0; i < count; i++)
                rootToId[i] = -1;

            int clusters = 0;
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (rootToId[root] < 0)
                    rootToId[root] = clusters++;
                clusterId[i] = rootToId[root];
            }

            return clusters;
        }

        // GetThreatBudget(n): the existing per-player-count threat curve, reused verbatim. Missing
        // BalanceConfig degrades to 1 (same graceful precedent as ResolveBudgetMultiplier).
        public static FP GetThreatBudget(BalanceConfig balance, int playerCount)
        {
            return balance == null ? FP._1 : balance.GetCoopGlobal(CoopGlobalKey.DirectorBudget, playerCount);
        }

        // Recomputed every COMBAT tick: raw-cluster the players, debounce the cohesive<->split MODE
        // (ClusterSplit/MergeDelay), then write the three runtime scalars the Director + reward
        // systems read. Outside combat (Breathing/Boss) everything resets to 1 so a Breathing-time
        // split never inflates threat or rewards.
        public static void UpdateRuntimeScalars(Frame f, DirectorConfig directorConfig, BalanceConfig balance, bool combatActive)
        {
            if (combatActive == false)
            {
                f.Global->DirectorSplitActive = false;
                f.Global->DirectorSplitTimer = FP._0;
                f.Global->SplitThreatMultiplier = FP._1;
                f.Global->PerEnemyXpScale = FP._1;
                f.Global->PerEnemyCoinScale = FP._1;
                return;
            }

            Span<FPVector3> pos = stackalloc FPVector3[MaxPlayers];
            Span<FPVector3> vel = stackalloc FPVector3[MaxPlayers];
            int playerCount = GatherPlayers(f, pos, vel);

            Span<int> clusterId = stackalloc int[MaxPlayers];
            int clusterCount = playerCount > 0 ? Cluster(pos, playerCount, directorConfig.ClusterDistance, clusterId) : 0;

            // Hysteresis on the split<->cohesive MODE only (two fields of state) - the anti-flicker
            // the design calls for, without persisting full per-player membership.
            bool rawSplit = clusterCount > 1;
            if (rawSplit != f.Global->DirectorSplitActive)
            {
                f.Global->DirectorSplitTimer += f.DeltaTime;
                FP delay = rawSplit ? directorConfig.ClusterSplitDelay : directorConfig.ClusterMergeDelay;

                if (f.Global->DirectorSplitTimer >= delay)
                {
                    f.Global->DirectorSplitActive = rawSplit;
                    f.Global->DirectorSplitTimer = FP._0;
                }
            }
            else
            {
                f.Global->DirectorSplitTimer = FP._0;
            }

            FP splitThreat = FP._1;
            if (f.Global->DirectorSplitActive && playerCount > 1)
                splitThreat = ComputeSplitThreatMultiplier(balance, directorConfig, clusterId, playerCount, clusterCount);

            FP extra = splitThreat - FP._1;
            FP xpMult = FP._1 + extra * directorConfig.SplitXpRewardFactor;
            FP coinMult = FP._1 + extra * directorConfig.SplitCoinRewardFactor;

            f.Global->SplitThreatMultiplier = splitThreat;
            f.Global->PerEnemyXpScale = splitThreat > FP._0 ? xpMult / splitThreat : FP._1;
            f.Global->PerEnemyCoinScale = splitThreat > FP._0 ? coinMult / splitThreat : FP._1;
        }

        // FinalTotal / BasePartyBudget, clamped to [1, MaxSplitThreatMultiplier]. BasePartyBudget
        // uses the LIVE clustered player count (not f.PlayerCount) so a single cluster is exactly 1.
        public static FP ComputeSplitThreatMultiplier(BalanceConfig balance, DirectorConfig directorConfig, Span<int> clusterId, int playerCount, int clusterCount)
        {
            FP basePartyBudget = GetThreatBudget(balance, playerCount);
            if (basePartyBudget <= FP._0)
                return FP._1;

            FP requested = FP._0;
            for (int c = 0; c < clusterCount; c++)
                requested += GetThreatBudget(balance, ClusterSize(clusterId, playerCount, c));

            FP finalTotal = FPMath.Min(requested, basePartyBudget * directorConfig.MaxSplitThreatMultiplier);
            FP mult = finalTotal / basePartyBudget;
            return mult < FP._1 ? FP._1 : mult;
        }

        public static int ClusterSize(Span<int> clusterId, int playerCount, int cluster)
        {
            int n = 0;
            for (int i = 0; i < playerCount; i++)
                if (clusterId[i] == cluster)
                    n++;
            return n;
        }

        // Builds this pulse's spawn fronts. Cohesive (or <=1 player): one front at the predicted
        // party centroid with the full (already split-scaled) TargetPressure - identical to the
        // pre-cluster Director. Split: one front per cluster, each centered on its own members and
        // given a share of TargetPressure proportional to its own GetThreatBudget weight.
        public static bool BuildAnchors(Frame f, SurvivalPhase phase, DirectorConfig directorConfig, BalanceConfig balance, out AnchorPlan plan)
        {
            plan = default;

            Span<FPVector3> pos = stackalloc FPVector3[MaxPlayers];
            Span<FPVector3> vel = stackalloc FPVector3[MaxPlayers];
            int playerCount = GatherPlayers(f, pos, vel);
            if (playerCount == 0)
                return false;

            plan.GlobalCentroid = PredictedCenter(pos, vel, playerCount, directorConfig.PredictionTime);

            bool split = f.Global->DirectorSplitActive && playerCount > 1;
            if (split == false)
            {
                plan.Count = 1;
                plan.Centers = new[] { plan.GlobalCentroid };
                plan.TargetPressure = new[] { phase.TargetPressure * ResolveSplitThreat(f) };
                return true;
            }

            Span<int> clusterId = stackalloc int[MaxPlayers];
            int clusterCount = Cluster(pos, playerCount, directorConfig.ClusterDistance, clusterId);

            FP basePartyBudget = GetThreatBudget(balance, playerCount);
            FP requested = FP._0;
            for (int c = 0; c < clusterCount; c++)
                requested += GetThreatBudget(balance, ClusterSize(clusterId, playerCount, c));

            FP finalTotal = FPMath.Min(requested, basePartyBudget * directorConfig.MaxSplitThreatMultiplier);
            FP shareScale = requested > FP._0 ? finalTotal / requested : FP._1; // proportional scale-down when over the cap

            plan.Count = clusterCount;
            plan.Centers = new FPVector3[clusterCount];
            plan.TargetPressure = new FP[clusterCount];

            for (int c = 0; c < clusterCount; c++)
            {
                FPVector3 posSum = FPVector3.Zero;
                FPVector3 velSum = FPVector3.Zero;
                int size = 0;

                for (int i = 0; i < playerCount; i++)
                {
                    if (clusterId[i] != c)
                        continue;
                    posSum += pos[i];
                    velSum += vel[i];
                    size++;
                }

                plan.Centers[c] = PredictedCenter(posSum, velSum, size, directorConfig.PredictionTime);

                FP clusterBudget = GetThreatBudget(balance, size) * shareScale;
                plan.TargetPressure[c] = basePartyBudget > FP._0
                    ? phase.TargetPressure * clusterBudget / basePartyBudget
                    : phase.TargetPressure;
            }

            return true;
        }

        // Sum of ResolveCost for ALL active Director enemies - used for the single cohesive front so
        // solo/grouped play keeps the Director's exact pre-cluster global-pressure gate.
        public static FP GlobalPressure(Frame f)
        {
            FP pressure = FP._0;
            var filtered = f.Filter<Enemy, EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out Enemy enemy, out EnemyLifecycle lifecycle))
            {
                if (lifecycle.State != EnemyLifecycleState.Active)
                    continue;

                pressure += f.FindAsset(enemy.EnemyData).ResolveCost(f);
            }

            return pressure;
        }

        // Sum of ResolveCost for active Director enemies within radius (flat XZ) of center - the
        // per-front analogue of GlobalPressure, used only when actually split into 2+ fronts.
        public static FP LocalPressure(Frame f, FPVector3 center, FP radius)
        {
            FP radiusSqr = radius * radius;
            FP pressure = FP._0;
            var filtered = f.Filter<Enemy, EnemyLifecycle, Transform3D>();

            while (filtered.Next(out EntityRef entity, out Enemy enemy, out EnemyLifecycle lifecycle, out Transform3D transform))
            {
                if (lifecycle.State != EnemyLifecycleState.Active)
                    continue;

                if (FlatSqrDistance(transform.Position, center) > radiusSqr)
                    continue;

                pressure += f.FindAsset(enemy.EnemyData).ResolveCost(f);
            }

            return pressure;
        }

        private static FP ResolveSplitThreat(Frame f)
        {
            FP mult = f.Global->SplitThreatMultiplier;
            return mult <= FP._0 ? FP._1 : mult;
        }

        private static FPVector3 PredictedCenter(Span<FPVector3> pos, Span<FPVector3> vel, int count, FP predictionTime)
        {
            FPVector3 posSum = FPVector3.Zero;
            FPVector3 velSum = FPVector3.Zero;
            for (int i = 0; i < count; i++)
            {
                posSum += pos[i];
                velSum += vel[i];
            }
            return PredictedCenter(posSum, velSum, count, predictionTime);
        }

        // TeamCenter + AverageVelocity * PredictionTime - the "moving combat bubble", now computed
        // per cluster instead of once for the whole party.
        private static FPVector3 PredictedCenter(FPVector3 posSum, FPVector3 velSum, int count, FP predictionTime)
        {
            if (count <= 0)
                return posSum;

            FPVector3 center = posSum / (FP)count;
            FPVector3 averageVelocity = velSum / (FP)count;
            return center + averageVelocity * predictionTime;
        }

        private static int Find(Span<int> parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void Union(Span<int> parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb)
                return;
            if (ra < rb)
                parent[rb] = ra;
            else
                parent[ra] = rb;
        }

        private static FP FlatSqrDistance(FPVector3 a, FPVector3 b)
        {
            FP dx = a.X - b.X;
            FP dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }
    }
}
