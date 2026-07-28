namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // How GroupSpawnerUtility turns "N members" into N deterministic offsets around the group
    // anchor - see GroupFormationUtility. Cluster/Circle/Arc/Line are pure index/count formulas
    // (no RNG draw at all, so re-running the same pulse with the same member count always yields
    // the same shape); Scatter is the one pattern that spends f.RNG rolls, same determinism
    // guarantee as any other RNG use in this codebase, just not reproducible from index alone.
    public enum GroupSpawnPattern
    {
        Cluster,
        Arc,
        Line,
        Scatter,
        Circle
    }

    // One enemy type plus how many copies of it belong to this group - offsets are no longer
    // authored here (compare the old fixed-LocalOffset design predating this file): GroupSpawnerUtility
    // generates them from EnemyGroupConfig.SpawnPattern/FormationRadius at spawn time. This is what
    // lets the same EnemyTierStatsConfig.Cost be the single source of truth for both pressure/refund AND
    // group affordability (see EnemyGroupConfig.ComputeCost) - a fixed AssetRef<EntityPrototype>
    // per member had no cheap way to read a Cost back out without materializing an entity.
    [Serializable]
    public struct GroupMemberEntry
    {
        public AssetRef<EnemyDataAsset> EnemyData;

        // >= 1. GroupSpawnerUtility flattens every member's Quantity into individual formation
        // slots before generating offsets, so e.g. {Fighter x3, Shooter x1} occupies 4 of the
        // pattern's slots, not 2.
        public Int32 Quantity;
    }

    // Where encounter design lives - CombatDirectorUtility only ever purchases one of these as a
    // whole, never an individual enemy, so composition is entirely up to whoever authors this
    // asset. Placement (formation shape) is separate from composition (Members) - see
    // GroupSpawnerUtility for how the two combine into final spawn positions.
    public class EnemyGroupConfig : AssetObject
    {
        public GroupMemberEntry[] Members;

        // Relative pick weight among groups that already passed every other TrySelectGroup check
        // (unlocked/affordable/cap/concurrency) - see CombatDirectorUtility.TrySelectGroup's
        // deterministic cumulative-weight roll. <= 0 excludes this group from the roll entirely
        // (a soft-disable a designer can flip without touching any phase's AllowedGroups list).
        public FP Weight = 1;

        // Second unlock gate alongside SurvivalPhase.AllowedGroups - a group must be BOTH listed
        // in the current phase's AllowedGroups AND within [MinimumSurvivalTime,
        // MaximumSurvivalTime] of f.Global->SurvivalTime to be selectable. MaximumSurvivalTime <= 0
        // means unlimited (same convention as MaxConcurrent below). Lets one group be authored to
        // fade out again later in a run (e.g. an early-game-only pack) without needing a second
        // SurvivalPhase just to drop it from AllowedGroups.
        public FP MinimumSurvivalTime;
        public FP MaximumSurvivalTime;

        // <= 0 = unlimited simultaneous live copies. Live count is recounted from
        // EnemyLifecycle.SourceGroup each pulse rather than kept in a maintained counter - cheap at
        // the scale this system runs at, and can never drift out of sync with reality.
        public Int32 MaxConcurrent;

        public GroupSpawnPattern SpawnPattern = GroupSpawnPattern.Cluster;

        // Radius (world units) the SpawnPattern generates member offsets within/along - meaning
        // varies per pattern (Cluster/Circle: disc/ring radius, Arc: same but only across a partial
        // sweep, Line: half-length of the line, Scatter: max jitter radius). See
        // GroupFormationUtility.ComputeLocalOffset.
        public FP FormationRadius = 3;

        // Reserved, not yet consumed - GroupSpawnerUtility's Milestone 1-2 implementation is
        // strictly all-or-nothing (see its own comment): any member failing validation fails the
        // whole group and creates nothing. A future partial-spawn milestone would check this before
        // discarding a group whose formation only partially fit.
        public bool AllowsPartialSpawn;

        // Authored Cost was removed on purpose - see docs/survival-director.md "Group Spawner
        // (Domain 3)". Cost is tier-driven (EnemyTierStatsConfig), so summing here means a
        // balance change to one tier's Cost is instantly reflected in every group that uses an
        // enemy of that tier, with no second number to keep in sync by convention.
        public FP ComputeCost(Frame f)
        {
            FP cost = FP._0;

            if (Members == null)
                return cost;

            for (int i = 0; i < Members.Length; i++)
            {
                AssetRef<EnemyDataAsset> enemyDataRef = Members[i].EnemyData;

                if (enemyDataRef.Id.IsValid == false)
                    continue;

                EnemyDataAsset enemyData = f.FindAsset(enemyDataRef);
                cost += EnemyTierStatsConfig.Resolve(f, enemyData.Tier).Cost * Members[i].Quantity;
            }

            return cost;
        }

        // Total formation slot count (sum of every Member's Quantity) - what GroupFormationUtility
        // needs as "count" for index-based patterns (Circle/Arc/Line/Cluster). 0 if Members is empty
        // or every Quantity is <= 0.
        public int ComputeMemberCount()
        {
            int count = 0;

            if (Members == null)
                return count;

            for (int i = 0; i < Members.Length; i++)
            {
                count += Math.Max(0, Members[i].Quantity);
            }

            return count;
        }
    }
}
