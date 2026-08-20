namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Domain 2 tunables (Combat Director) - kept separate from SurvivalConfig since pacing
    // (Domain 1: budget/pressure/cap/phases) and spawning mechanics (Domain 2: where/how) are
    // different balancing concerns even though CombatDirectorSystem runs them back to back.
    public class DirectorConfig : AssetObject
    {
        // The one shared "blank" enemy prototype every Director purchase is created from -
        // GroupSpawnerUtility.SpawnMember does f.Create(EnemyPrototype) then sets Enemy->EnemyData
        // itself per member (see that method's own comment for why: one generic prototype, not one
        // baked prototype per enemy type, keeps EnemyTierStatsConfig.Cost as the single source of
        // truth group cost/pressure/refund all read - a per-type baked AssetRef<EntityPrototype> on every
        // GroupMemberEntry had no cheap way to read a Cost back out without materializing an
        // entity). Point this at the same generic prefab used elsewhere in the project (e.g.
        // BasicEnemy) - assigned once in the Editor, not authored per group.
        public AssetRef<EntityPrototype> EnemyPrototype;

        // How far ahead of the team's current average velocity the spawn ring is centered -
        // see CombatDirectorUtility.ComputePredictedCombatCenter.
        public FP PredictionTime = 2;

        public FP SpawnRingRadiusMin = 8;
        public FP SpawnRingRadiusMax = 14;

        // Safety valve on the pulse's "repeat until purchase limit" loop - without this, a
        // low-cost group against a high per-pulse budget could purchase an unbounded number of
        // times in a single tick.
        public Int32 MaxPurchasesPerPulse = 3;

        // How many candidate group anchors GroupSpawnerUtility.TrySpawnGroup tries (each a fresh
        // ring position) before giving up on this purchase entirely. One anchor attempt validates
        // every member of the group at once (see GroupSpawnerUtility) and either fully succeeds or
        // is discarded whole - no per-member retry/relaxation yet (see the design doc's Milestone 3+
        // roadmap for AngleAttemptStep/DistanceRelaxationStep/FormationRadiusRelaxation-style
        // escalation).
        public Int32 MaxGroupSpawnAttempts = 8;

        // Chunk types no group member is ever allowed to land inside (e.g. Traversal, so corridors
        // stay clear) - checked in GroupSpawnerUtility.TryValidateMember via
        // EnemyPathfindingUtility.TryFindContainingChunk. Empty/unassigned (the default) means
        // unrestricted, same "empty means no rule" convention LevelConfig.ChunkPool-adjacent
        // authoring already uses - every group spawns exactly as before this field existed.
        public ChunkType[] ForbiddenSpawnChunkTypes;

        // --- Co-op player-cluster split spawning (see docs/survival-director.md "Player clusters" +
        // PlayerClusterDirectorUtility). Nothing here is hardcoded to 4 players; every formula is
        // driven by live cluster sizes fed through the existing per-player-count threat curve
        // (BalanceConfig CoopGlobalKey.DirectorBudget, reused verbatim as "GetThreatBudget(n)"). ---

        // Two players farther apart than this (flat XZ) are treated as separate combat fronts;
        // closer, they share one. Keep >= SpawnRingRadiusMax + RelevantRange so a single centroid
        // still reaches both before they count as split.
        public FP ClusterDistance = 30;

        // Radius around a cluster's center within which an active enemy counts toward THAT cluster's
        // local pressure (the per-front analogue of the global TargetPressure gate). ~RelevantRange.
        public FP ClusterPressureRadius = 16;

        // Cap on how much extra total threat splitting may request, as a multiple of the normal
        // same-party budget: FinalTotal = min(sum of per-cluster budgets, base * this). 1 disables
        // the bonus (spawns are only redistributed, never increased); 1.40 = up to +40%.
        public FP MaxSplitThreatMultiplier = FP.FromString("1.40");

        // Extra XP/Coin for the extra split threat, as a fraction of it: PerEnemyReward =
        // (1 + (SplitThreatMultiplier-1)*Factor) / SplitThreatMultiplier, applied by CurrencyOrbSystem
        // so splitting gives a little more progression for the added risk, NOT proportional to the
        // added enemies (0 = splitting yields no extra reward, only redistributed spawns).
        public FP SplitXpRewardFactor = FP.FromString("0.25");
        public FP SplitCoinRewardFactor = FP.FromString("0.10");

        // Anti-flicker: a cohesive<->split change must persist this long before it commits, so
        // wandering near ClusterDistance doesn't thrash the budget/reward scalars. Splitting commits
        // slower than merging (rejoining should feel immediate).
        public FP ClusterSplitDelay = 2;
        public FP ClusterMergeDelay = 1;
    }
}
