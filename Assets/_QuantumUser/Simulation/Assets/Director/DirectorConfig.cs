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
        // ring position) before giving up on this purchase entirely - same role as
        // LevelGenerationSystem.MaxAttemptsPerRequest. One anchor attempt validates every member of
        // the group at once (see GroupSpawnerUtility) and either fully succeeds or is discarded
        // whole - no per-member retry/relaxation yet (see the design doc's Milestone 3+ roadmap for
        // AngleAttemptStep/DistanceRelaxationStep/FormationRadiusRelaxation-style escalation).
        public Int32 MaxGroupSpawnAttempts = 8;
    }
}
