namespace Quantum
{
    using Photon.Deterministic;

    // Backs EnemyDataAsset.Stats.UseWaypointDetour (see EnemySystem.UpdateChasing): while the
    // direct line to the target is wall-blocked, locates the Chunk the enemy is standing in, finds
    // the nearest baked waypoint to both self and target within it, and A*'s a route across that
    // single chunk's Chunk.Waypoints graph. Deliberately single-chunk only - Chunk.Waypoints has no
    // cross-chunk links yet, so a target in a different chunk is approached via whichever of this
    // chunk's own waypoints lands nearest it, not a real end-to-end route.
    public static unsafe class EnemyPathfindingUtility
    {
        // Matches Chunk.Waypoints' fixed array size in Chunk.qtn - keep in sync if that ever changes.
        private const int MaxWaypoints = 16;

        // Called from EnemySystem.UpdateChasing when EnemyDataAsset.Stats.UseWaypointDetour is set.
        // Returns true (with the direction to steer) only while the direct line to the target is
        // wall-blocked and a detour was actually resolved; false means "ignore this, use your own
        // Stats.Movement direction instead" - covers clear line-of-sight, no EnemyWaypointPath
        // component authored on this prototype, or no chunk/route found for a detour. Line-of-sight
        // is checked fresh every call (not cached), so a detour in progress drops itself and hands
        // back to normal movement the instant the target comes back into view, even mid-path.
        public static bool TryGetDetourDirection(Frame f, EntityRef self, EnemyDataAsset data, FPVector3 selfPosition, FPVector3 targetPosition, out FPVector2 direction)
        {
            direction = default;

            if (f.Unsafe.TryGetPointer<EnemyWaypointPath>(self, out var path) == false)
                return false;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 toTarget = targetPosition - selfPosition;
            bool hasLineOfSight = EnemyMovementUtility.IsBlockedByWall(f, selfPosition, toTarget, toTarget.Magnitude, groundLayerMask) == false;

            if (hasLineOfSight == true)
            {
                // Target's back in view - drop whatever detour was in progress so a fresh one gets
                // resolved next time line-of-sight breaks, rather than resuming a stale route.
                path->Count = 0;
                return false;
            }

            if (path->Count == 0 && TryBuildPath(f, selfPosition, targetPosition, path) == false)
                return false;

            FPVector3 waypoint = path->Waypoints[path->Cursor];

            if ((waypoint - selfPosition).SqrMagnitude <= data.Stats.WaypointArrivalDistance * data.Stats.WaypointArrivalDistance)
            {
                path->Cursor++;

                if (path->Cursor >= path->Count)
                {
                    // Ran out of detour without regaining line-of-sight (the target moved) - hand
                    // back to the caller's own movement for this tick; next tick's line-of-sight
                    // check resolves a fresh detour if that's still blocked too.
                    path->Count = 0;
                    return false;
                }

                waypoint = path->Waypoints[path->Cursor];
            }

            direction = DirectionTo(selfPosition, waypoint);
            return true;
        }

        private static FPVector2 DirectionTo(FPVector3 from, FPVector3 to)
        {
            FPVector2 delta = new FPVector2(to.X - from.X, to.Z - from.Z);
            return delta.SqrMagnitude > FP._0 ? delta.Normalized : default;
        }

        // The target itself might be outside this chunk entirely - pathfinding here is single-
        // chunk only (see this class' own comment), so this resolves to whichever of this chunk's
        // waypoints lands nearest the target regardless, which is the best a single-chunk detour
        // can do.
        private static bool TryBuildPath(Frame f, FPVector3 selfPosition, FPVector3 targetPosition, EnemyWaypointPath* path)
        {
            if (TryFindContainingChunk(f, selfPosition, out EntityRef chunkEntity) == false)
                return false;

            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(chunkEntity);
            Transform3D* chunkTransform = f.Unsafe.GetPointer<Transform3D>(chunkEntity);

            if (TryFindNearestWaypoint(chunk, chunkTransform, selfPosition, out int startIndex) == false)
                return false;

            if (TryFindNearestWaypoint(chunk, chunkTransform, targetPosition, out int goalIndex) == false)
                return false;

            FPVector3* buffer = stackalloc FPVector3[MaxWaypoints];
            int count = TryFindPath(chunk, chunkTransform, startIndex, goalIndex, buffer, MaxWaypoints);

            if (count == 0)
                return false;

            for (int i = 0; i < count; i++)
            {
                path->Waypoints[i] = buffer[i];
            }

            path->Count = (byte)count;
            path->Cursor = 0;
            return true;
        }

        // Transforms the query point into each chunk's own local (unrotated, min-corner-pivoted)
        // space and tests it against the chunk's authored footprint directly off Transform3D,
        // rather than duplicating LevelGenerationSystem's own grid-placement math - correct
        // regardless of whatever that system's rotation ends up actually applying at runtime.
        public static bool TryFindContainingChunk(Frame f, FPVector3 position, out EntityRef chunkEntity)
        {
            var filtered = f.Filter<Chunk, Transform3D>();

            while (filtered.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
            {
                FPVector3 local = FPQuaternion.Inverse(transform.Rotation) * (position - transform.Position);

                if (local.X >= FP._0 && local.X <= chunk.ChunkSizeWidth &&
                    local.Z >= FP._0 && local.Z <= chunk.ChunkSizeDepth)
                {
                    chunkEntity = entity;
                    return true;
                }
            }

            chunkEntity = EntityRef.None;
            return false;
        }

        public static FPVector3 WaypointWorldPosition(Chunk* chunk, Transform3D* chunkTransform, int index)
        {
            return chunkTransform->Position + chunkTransform->Rotation * chunk->Waypoints[index].LocalPosition;
        }

        public static bool TryFindNearestWaypoint(Chunk* chunk, Transform3D* chunkTransform, FPVector3 worldPosition, out int index)
        {
            index = -1;
            FP bestSqrDistance = FP.MaxValue;

            for (int i = 0; i < chunk->WaypointCount; i++)
            {
                FP sqrDistance = (WaypointWorldPosition(chunk, chunkTransform, i) - worldPosition).SqrMagnitude;

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    index = i;
                }
            }

            return index >= 0;
        }

        // Plain A* over a chunk's baked waypoint graph - at most MaxWaypoints nodes, cheap enough
        // for an O(n^2) open-set scan with no heap. Writes the resolved route (start through goal,
        // inclusive) as world positions into result and returns how many entries were written, or 0
        // if start==goal or no route exists (e.g. the two halves were never connected by
        // ChunkWaypointBaker - see its own IsPathBlocked).
        public static int TryFindPath(Chunk* chunk, Transform3D* chunkTransform, int startIndex, int goalIndex, FPVector3* result, int resultCapacity)
        {
            if (startIndex == goalIndex)
                return 0;

            int nodeCount = chunk->WaypointCount;
            FP* gScore = stackalloc FP[MaxWaypoints];
            FP* fScore = stackalloc FP[MaxWaypoints];
            int* cameFrom = stackalloc int[MaxWaypoints];
            bool* closed = stackalloc bool[MaxWaypoints];
            bool* open = stackalloc bool[MaxWaypoints];

            for (int i = 0; i < nodeCount; i++)
            {
                gScore[i] = FP.MaxValue;
                fScore[i] = FP.MaxValue;
                cameFrom[i] = -1;
                closed[i] = false;
                open[i] = false;
            }

            FPVector3 goalPosition = WaypointWorldPosition(chunk, chunkTransform, goalIndex);

            gScore[startIndex] = FP._0;
            fScore[startIndex] = FPVector3.Distance(WaypointWorldPosition(chunk, chunkTransform, startIndex), goalPosition);
            open[startIndex] = true;

            while (true)
            {
                int current = -1;
                FP bestF = FP.MaxValue;

                for (int i = 0; i < nodeCount; i++)
                {
                    if (open[i] == true && fScore[i] < bestF)
                    {
                        bestF = fScore[i];
                        current = i;
                    }
                }

                if (current == -1)
                    return 0;

                if (current == goalIndex)
                    return ReconstructPath(chunk, chunkTransform, cameFrom, current, result, resultCapacity);

                open[current] = false;
                closed[current] = true;

                uint mask = chunk->Waypoints[current].ConnectionMask;

                for (int neighbor = 0; neighbor < nodeCount; neighbor++)
                {
                    if (neighbor == current || closed[neighbor] == true || (mask & (1u << neighbor)) == 0)
                        continue;

                    FP tentativeG = gScore[current] + FPVector3.Distance(
                        WaypointWorldPosition(chunk, chunkTransform, current),
                        WaypointWorldPosition(chunk, chunkTransform, neighbor));

                    if (tentativeG < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        fScore[neighbor] = tentativeG + FPVector3.Distance(WaypointWorldPosition(chunk, chunkTransform, neighbor), goalPosition);
                        open[neighbor] = true;
                    }
                }
            }
        }

        // cameFrom chains back from goal to start - walked once to count, then again to write
        // forward (start-first) order into result, since a pathological chain could in principle
        // exceed resultCapacity (never actually happens at MaxWaypoints nodes, but stay safe rather
        // than overrun the caller's buffer).
        private static int ReconstructPath(Chunk* chunk, Transform3D* chunkTransform, int* cameFrom, int goalIndex, FPVector3* result, int resultCapacity)
        {
            int length = 0;

            for (int node = goalIndex; node != -1; node = cameFrom[node])
            {
                length++;
            }

            if (length > resultCapacity)
                return 0;

            int writeIndex = length - 1;

            for (int node = goalIndex; node != -1; node = cameFrom[node])
            {
                result[writeIndex] = WaypointWorldPosition(chunk, chunkTransform, node);
                writeIndex--;
            }

            return length;
        }
    }
}
