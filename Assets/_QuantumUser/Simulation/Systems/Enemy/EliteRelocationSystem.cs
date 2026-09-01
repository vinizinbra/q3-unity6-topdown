namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // "An Elite should never be forgotten" - EnemyLifecycleSystem already keeps an Elite Active
    // forever regardless of distance (see IsRelevant), which is exactly the problem: nothing else
    // ever repositions one that's leashed off, walled off, or otherwise stuck away from every
    // player. Left alone, EnemySystem.UpdateIdle retries target acquisition every tick but never
    // moves while Idle (see its own comment), so an Elite that ends up farther than
    // EnemyDataAsset.AI.DetectionRange from every player sits there indefinitely - genuinely lost,
    // and (per docs/survival-director.md) able to hold up an Elite-phase encounter forever since
    // nothing ever kills it.
    //
    // Tracks Enemy.LostTimer: reset to 0 whenever any player is within LifecycleConfig.
    // EliteLostRange, otherwise counted up. Once it crosses EliteLostTeleportDelay, the Elite is
    // teleported to a ring position around whichever real player is currently closest - same
    // RandomPositionInRing + TryFindGroundHeight idiom GroupSpawnerUtility uses to place a fresh
    // spawn near players, reusing DirectorConfig's own ring radii rather than authoring a second
    // copy. Registered right after EnemyFallSystem so it also reads this tick's fully-resolved
    // Transform3D.Position before anything else (projectiles, area damage) acts on the old one.
    [Preserve]
    public unsafe class EliteRelocationSystem : SystemMainThreadFilter<EliteRelocationSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);

            if (data == null || data.Tier != EnemyTier.Elite)
                return;

            LifecycleConfig lifecycleConfig = f.FindAsset(f.RuntimeConfig.LifecycleConfig);

            if (PlayerQueryUtility.IsAnyPlayerWithinFlatRange(f, filter.Transform3D->Position, lifecycleConfig.EliteLostRange) == true)
            {
                filter.Enemy->LostTimer = FP._0;
                return;
            }

            filter.Enemy->LostTimer += f.DeltaTime;

            if (filter.Enemy->LostTimer < lifecycleConfig.EliteLostTeleportDelay)
                return;

            if (TryFindNearestAlivePlayer(f, filter.Transform3D->Position, out FPVector3 playerPosition) == false)
            {
                // Nobody alive/reachable to teleport toward (e.g. the whole party is Downed/KO) -
                // leave LostTimer running so this fires again the very next tick once someone can
                // be found, rather than silently resetting and losing the whole wait.
                return;
            }

            if (TryResolveTeleportDestination(f, playerPosition, out FPVector3 destination) == false)
                return;

            Log.Debug($"[EliteRelocation] {filter.Entity} ({data.name}) was lost for {filter.Enemy->LostTimer}s - teleporting to {destination}");

            filter.Transform3D->Position = destination;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;
            filter.Enemy->LostTimer = FP._0;
        }

        // Real players only (not Sentries) - "closer to the hero" means an actual person, same
        // PlayerLink-only candidate set PlayerQueryUtility.GatherPlayers documents for anything
        // FRIENDLY. Skips a Downed/KO player, same reasoning EnemySystem.UpdateChasing already
        // applies when picking/keeping a target - relocating next to someone who can't fight back
        // or be revived by a nearby ally isn't the point.
        private static bool TryFindNearestAlivePlayer(Frame f, FPVector3 origin, out FPVector3 position)
        {
            Span<EntityRef> buffer = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int count = PlayerQueryUtility.GatherPlayers(f, buffer);

            position = default;
            EntityRef nearest = EntityRef.None;
            FP nearestSqrDistance = default;

            for (int i = 0; i < count; i++)
            {
                EntityRef candidate = buffer[i];

                if (PlayerLifeStateUtility.IsIncapacitated(f, candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                    continue;

                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(origin, transform->Position);

                if (nearest == EntityRef.None || sqrDistance < nearestSqrDistance)
                {
                    nearest = candidate;
                    nearestSqrDistance = sqrDistance;
                    position = transform->Position;
                }
            }

            return nearest != EntityRef.None;
        }

        // Same ring-then-ground-correct idiom GroupSpawnerUtility.TrySpawnGroup uses to place a
        // fresh spawn near a player, reusing DirectorConfig's own SpawnRingRadiusMin/Max/
        // MaxGroupSpawnAttempts rather than authoring a second copy just for this. Deliberately
        // skips GroupSpawnerUtility's own clearance/forbidden-chunk checks - this is a rare
        // corrective teleport (not routine spawn placement), and physics settles a body dropped on
        // top of another one anyway.
        private static bool TryResolveTeleportDestination(Frame f, FPVector3 playerPosition, out FPVector3 destination)
        {
            DirectorConfig directorConfig = f.FindAsset(f.RuntimeConfig.DirectorConfig);
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            for (int attempt = 0; attempt < directorConfig.MaxGroupSpawnAttempts; attempt++)
            {
                FPVector3 candidate = EnemyMovementUtility.RandomPositionInRing(
                    f, playerPosition, directorConfig.SpawnRingRadiusMin, directorConfig.SpawnRingRadiusMax);

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidate, groundLayerMask, out FP groundY) == false)
                    continue;

                destination = new FPVector3(candidate.X, groundY, candidate.Z);
                return true;
            }

            destination = default;
            return false;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public Transform3D* Transform3D;
            public PhysicsBody3D* PhysicsBody3D;
        }
    }
}
