namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Every way a bot moves - pathfinding, steering, void avoidance, heading smoothing and the
    // leash teleport. BotInputSystem decides WHERE to go; this decides how to get there without
    // walking off the map. See docs/bots.md "Navigation".
    //
    //   1. BotNavGrid - a level-wide 1x1 walkability grid (ground height per cell, climb/drop
    //      rules, shoreline cells penalized). If the grid says the bot can walk straight to the
    //      target, it does; otherwise A* + string-pulling gives a few straight legs (BotBrain.Path).
    //   2. SteerAroundWalls slides along anything the grid didn't capture (props, barrels).
    //   3. TryFindSafeDirection still rejects any direction whose ground runs out - the last line
    //      of defence against water, trying mirrored deflections, preferring the side it turned to
    //      last time so it doesn't zig-zag along a lip.
    //   4. ApplyHeading blends last tick's heading in so small corrections don't flip the bot back
    //      and forth every tick.
    public static unsafe class BotNavigation
    {
        // Every bot raycast that reads a hit's height/normal. HitDynamics because some level geometry
        // in this project is a genuinely dynamic entity (see EnemyMovementUtility.TryFindGroundHeight),
        // ComputeDetailedInfo because Hit3D.Point/Normal only hold real data with it (see
        // WeaponSystem.ResolveHitscanPoint).
        public const QueryOptions BotQuery = QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics | QueryOptions.ComputeDetailedInfo;

        // Must match BotBrain.Path's own qtn array size.
        public const int MaxPathWaypoints = 32;

        // Re-plan when the goal has moved this far from where the current path leads, and at least
        // this often while following a path (the world - enemies, the leader - keeps moving).
        private static readonly FP RepathDistance = FP._2 + FP._0_50;
        private static readonly FP RepathInterval = FP._1 + FP._0_50;

        public static readonly FP WaypointArrivalDistance = FP._0_50 + FP._0_25;

        // Wall probe - matches EnemyMovementUtility.MoveInDirection's scale for a player-sized body.
        private static readonly FP WallProbeDistance = FP._2;
        private static readonly FP WallProbeRadius = FP._0_50;

        // Ledge probe. Reaches FURTHER than the player's own auto-hop probe
        // (MovementDataAsset.EdgeProbeDistance, 0.75): auto-hop reacts to "no ground ahead" by
        // JUMPING, so the bot has to turn away before that ever fires.
        private static readonly FP LedgeProbeDistance = FP._1 + FP._0_50;

        // Deepest drop still treated as walkable-down ground rather than a pit.
        public static readonly FP LedgeMaxDropDistance = 4;

        // The player's auto-mantle (PlayerMovementProcessor.TryDetectMantle): a ray at the feet
        // blocked within MantleProbeDistance, and a ray at MovementDataAsset.MaxLedgeHeight (1) clear.
        // The bot mirrors that test so it walks INTO a climbable step instead of sliding along it.
        // Kept equal to the asset's values - the player's own processor is what actually climbs.
        private static readonly FP MantleLedgeHeight = FP._1;
        // As far as the wall slide below can see (WallProbeDistance + WallProbeRadius) - shorter, and
        // the slide would turn the bot away from a climbable step before this ever noticed it.
        private static readonly FP MantleProbeReach = FP._2 + FP._0_50;
        private static readonly FP MantleFootHeight = FP._0_05;
        private const string NotJumpableLayerName = "GroundNotJumpable";

        // Once a climbable step is seen, keep walking straight into it this long (see BotBrain) -
        // walking straight at it is all the player processor's auto-mantle needs.
        private static readonly FP ClimbCommitTime = FP._0_50 - FP._0_10;

        // A gap with ground on the far side within this distance is crossable - this only exists
        // for chunk SEAMS (sub-unit gaps between placed chunks). Kept at 1 rather than the auto-hop's
        // full ~3-unit reach: anything wider is almost always a river/lake shoreline, and treating a
        // narrow lake as "just hop it" is exactly how bots used to end up in the water.
        private static readonly FP LedgeMaxCrossableGap = FP._1;
        private static readonly FP LedgeGapScanStep = FP._0_25;

        // Two extra ledge probes this far either side of the centre line - a single centre ray let a
        // bot walking ALONG a lip drift half its body over the edge and slide off.
        private static readonly FP LedgeLateralOffset = FP._0_50 - FP._0_10;

        // Mirrored deflection pairs tried when the direct route runs off an edge. Immutable constant
        // data, never written at runtime, so static is safe in the simulation.
        private static readonly FP[] LedgeDeflectionAngles = { 35, 70, 105 };

        // How much of last tick's heading survives into this tick's (see ApplyHeading). Enough to
        // swallow one-tick steering flips, small enough that a real turn still lands within ~0.1s.
        private static readonly FP HeadingCarry = FP._0_50;

        // Moves toward targetPosition this tick. Returns false only when there is no way there at
        // all (no grid route, or every direction off a ledge with no route) - the caller's "stuck"
        // signal.
        public static bool TryMoveToward(Frame f, ref BotInputSystem.Filter filter, FPVector3 targetPosition, bool run)
        {
            BotBrain* brain = filter.Brain;
            FPVector3 position = filter.Transform->Position;
            BotNavGrid grid = BotNavGrid.Get(f);

            brain->RepathTimer -= f.DeltaTime;

            // No grid yet (level still settling) - plain safe steering.
            if (grid == null)
                return SteerToward(f, ref filter, targetPosition - position, run);

            if (grid.IsStraightWalkable(position, targetPosition) == true)
            {
                ClearPaths(brain);

                if (SteerToward(f, ref filter, targetPosition - position, run, wallSlide: false) == true)
                    return true;
            }

            bool needsPath = brain->PathCursor >= brain->PathCount
                || brain->RepathTimer <= FP._0
                || FlatDistance(brain->PathGoal, targetPosition) > RepathDistance;

            if (needsPath == true && Plan(grid, brain, position, targetPosition) == false)
                return false;

            // Skip ahead: arrived at this waypoint, or the next one is already straight-walkable
            // (the bot got pushed along, or cut a corner) - at most one look-ahead check per tick.
            if (FlatDistance(position, brain->Path[brain->PathCursor]) <= WaypointArrivalDistance
                || (brain->PathCursor + 1 < brain->PathCount && grid.IsStraightWalkable(position, brain->Path[brain->PathCursor + 1]) == true))
            {
                brain->PathCursor++;

                // Ran off the end of a stored (possibly truncated) route - plan again from here.
                if (brain->PathCursor >= brain->PathCount && Plan(grid, brain, position, targetPosition) == false)
                    return false;
            }

            if (SteerToward(f, ref filter, brain->Path[brain->PathCursor] - position, run, wallSlide: false) == true)
                return true;

            // The leg itself is blocked by something the grid doesn't know (a barrel, a knocked-off
            // position): re-plan next tick rather than declaring the whole goal unreachable.
            brain->RepathTimer = FP._0;
            return true;
        }

        private static bool Plan(BotNavGrid grid, BotBrain* brain, FPVector3 position, FPVector3 targetPosition)
        {
            int count = grid.FindPath(position, targetPosition, brain->Path.GetPointer(0), MaxPathWaypoints);

            brain->PathCount = (byte)count;
            brain->PathCursor = 0;
            brain->PathGoal = targetPosition;
            brain->RepathTimer = RepathInterval;

            return count > 0;
        }

        // Steers along a world direction. Returns false when every candidate runs off a ledge.
        //
        // wallSlide: SteerAroundWalls' sphere-cast sees Ground-layer geometry from 2+ units away and
        // turns the bot along it. When the move came from the nav grid (which already knows every
        // Ground-layer wall AND which ones are climbable steps) that slide only fights the grid - it
        // kept bots shuffling sideways 2 units short of a 1-unit step they could have walked up - so
        // grid-driven moves pass false. Free directions (Retreat, strafing) keep it.
        public static bool SteerToward(Frame f, ref BotInputSystem.Filter filter, FPVector3 delta, bool run, bool wallSlide = true)
        {
            FPVector2 direction = new FPVector2(delta.X, delta.Z);

            if (direction == default)
                return true;

            direction = direction.Normalized;

            FPVector3 position = filter.Transform->Position;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            BotBrain* brain = filter.Brain;
            bool grounded = filter.KCC->Data.IsGrounded;

            brain->ClimbCommitTimer -= f.DeltaTime;

            // A step the bot can climb is not a wall: commit to walking straight into it - the
            // player processor's auto-mantle does the rest - instead of letting the wall slide turn
            // the bot along it. Only while the committed direction still roughly agrees with where
            // we want to go.
            if (wallSlide == true && grounded == true && IsClimbableStepAhead(f, position, direction, groundLayerMask) == true)
            {
                brain->ClimbCommitTimer = ClimbCommitTime;
                brain->ClimbDirection = direction;
            }

            if (wallSlide == true && brain->ClimbCommitTimer > FP._0 && FPVector2.Dot(brain->ClimbDirection, direction) > FP._0_50)
            {
                brain->Data.Direction = brain->ClimbDirection;
                brain->Data.Run = run;
                brain->Heading = brain->ClimbDirection;
                return true;
            }

            if (wallSlide == true)
            {
                // Probe from knee height - a cast flush with the floor reports the floor itself and
                // never finds the real wall behind it.
                FPVector3 probeOrigin = position + FPVector3.Up * WallProbeRadius;

                direction = EnemyMovementUtility.SteerAroundWalls(f, probeOrigin, direction, WallProbeDistance, WallProbeRadius, groundLayerMask);
            }

            // Runs second on purpose - a direction slid along a wall can point off a ledge just as
            // easily as the original did.
            if (TryFindSafeDirection(f, filter.Transform->Position, filter.KCC->Data.IsGrounded, direction, filter.Brain, out FPVector2 safeDirection) == false)
                return false;

            filter.Brain->Data.Direction = ApplyHeading(f, filter.Transform->Position, filter.KCC->Data.IsGrounded, filter.Brain, safeDirection);
            filter.Brain->Data.Run = run;
            return true;
        }

        // Blends last tick's heading into this tick's decision. A near-reversal snaps straight to the
        // new direction (blending through zero would just stall), and the blend is only used if it is
        // itself safe - it can cut a corner the raw safe direction deliberately avoided.
        private static FPVector2 ApplyHeading(Frame f, FPVector3 position, bool isGrounded, BotBrain* brain, FPVector2 direction)
        {
            FPVector2 previous = brain->Heading;
            FPVector2 result = direction;

            if (previous != default && FPVector2.Dot(previous, direction) > -FP._0_25)
            {
                FPVector2 blended = previous * HeadingCarry + direction * (FP._1 - HeadingCarry);

                if (blended.SqrMagnitude > FP._0_01)
                {
                    blended = blended.Normalized;

                    if (isGrounded == false || IsDirectionSafe(f, position, blended, EnemyMovementUtility.GetGroundLayerMask(f)) == true)
                        result = blended;
                }
            }

            brain->Heading = result;
            return result;
        }

        // Direct route first, then mirrored deflections out to 105 degrees (walking ALONG a lip
        // rather than into it), the side used last time first. Fails only when every candidate is
        // a pit.
        private static bool TryFindSafeDirection(Frame f, FPVector3 position, bool isGrounded, FPVector2 desired, BotBrain* brain, out FPVector2 safeDirection)
        {
            safeDirection = desired;

            // Airborne: these probes measure ground under a WALKING path, meaningless mid-jump.
            if (isGrounded == false || desired == default)
                return true;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (IsDirectionSafe(f, position, desired, groundLayerMask) == true)
                return true;

            int side = brain->DeflectSide >= 0 ? 1 : -1;

            for (int i = 0; i < LedgeDeflectionAngles.Length; i++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int sign = pass == 0 ? side : -side;
                    FPVector2 candidate = Rotate(desired, LedgeDeflectionAngles[i] * sign);

                    if (IsDirectionSafe(f, position, candidate, groundLayerMask) == false)
                        continue;

                    brain->DeflectSide = sign;
                    safeDirection = candidate;
                    return true;
                }
            }

            safeDirection = default;
            return false;
        }

        // Safe = the centre line AND both lateral lines each have ground ahead within a survivable
        // drop, or a seam-sized gap with ground just beyond it.
        public static bool IsDirectionSafe(Frame f, FPVector3 position, FPVector2 direction, int groundLayerMask)
        {
            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Y);
            FPVector3 lateral = new FPVector3(-direction.Y, FP._0, direction.X) * LedgeLateralOffset;

            return IsLineSafe(f, position, flatDirection, groundLayerMask)
                && IsLineSafe(f, position + lateral, flatDirection, groundLayerMask)
                && IsLineSafe(f, position - lateral, flatDirection, groundLayerMask);
        }

        // The ray starts ABOVE the climb height and looks down, so a step up (a 1-unit ledge) reads as
        // ground - a ray starting at the feet starts inside the step's collider and sees nothing.
        // Only answers "is there floor, or is this a void" - a tall wall here still reads as "floor"
        // (its top), walls are SteerAroundWalls' job.
        private static bool IsLineSafe(Frame f, FPVector3 position, FPVector3 flatDirection, int groundLayerMask)
        {
            FP probeHeight = MantleLedgeHeight + FP._0_50;
            FPVector3 probeOrigin = position + flatDirection * LedgeProbeDistance + FPVector3.Up * probeHeight;
            Hit3D? hit = f.Physics3D.Raycast(probeOrigin, FPVector3.Down, probeHeight + LedgeMaxDropDistance, groundLayerMask, BotQuery);

            if (hit.HasValue == true)
                return true;

            if (EnemyMovementUtility.TryFindGapLanding(f, position, flatDirection, LedgeProbeDistance,
                    LedgeProbeDistance + LedgeMaxCrossableGap, LedgeGapScanStep, groundLayerMask, out FPVector3 landing) == false)
            {
                return false;
            }

            // TryFindGapLanding looks ~20 units down - hold the landing to the same drop limit, or a
            // deep-but-floored pit reads as a crossable seam.
            return landing.Y >= position.Y - LedgeMaxDropDistance;
        }

        // Mirror of PlayerMovementProcessor.TryDetectMantle, reaching a little further so the bot
        // commits before the processor's own 0.8 probe: blocked at the feet by a near-vertical face
        // (a ramp is just walked up), clear at the ledge height, and not a GroundNotJumpable obstacle
        // (those are never mantled).
        private static bool IsClimbableStepAhead(Frame f, FPVector3 position, FPVector2 direction, int groundLayerMask)
        {
            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Y);
            QueryOptions options = BotQuery;
            int notJumpableMask = f.Layers.GetLayerMask(NotJumpableLayerName);

            FPVector3 footOrigin = position + FPVector3.Up * MantleFootHeight;
            Hit3D? foot = f.Physics3D.Raycast(footOrigin, flatDirection, MantleProbeReach, groundLayerMask, options);

            if (foot.HasValue == false || FPMath.Abs(foot.Value.Normal.Y) > FP._0_50)
                return false;

            if (notJumpableMask != 0 && f.Physics3D.Raycast(footOrigin, flatDirection, MantleProbeReach, notJumpableMask, options).HasValue == true)
                return false;

            FPVector3 ledgeOrigin = position + FPVector3.Up * MantleLedgeHeight;

            return f.Physics3D.Raycast(ledgeOrigin, flatDirection, MantleProbeReach, groundLayerMask | notJumpableMask, options).HasValue == false;
        }

        // True when there's standable ground at `point`, no more than LedgeMaxDropDistance below and
        // `maxRise` above referenceY. Used for formation slots and dash landings.
        public static bool IsStandable(Frame f, FPVector3 point, FP referenceY, FP maxRise)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, point, groundLayerMask, out FP groundY) == false)
                return false;

            return groundY >= referenceY - LedgeMaxDropDistance && groundY <= referenceY + maxRise;
        }

        // A Dash moves the hero `distance` along `direction` with the KCC disabled (DashSkillData),
        // so mid-path gaps don't matter - only where it ENDS. Checks the landing and both sides of
        // it, plus the midpoint (a wall can cut the dash short there).
        public static bool IsDashLandingSafe(Frame f, FPVector3 position, FPVector2 direction, FP distance)
        {
            FPVector3 flat = new FPVector3(direction.X, FP._0, direction.Y);
            FPVector3 lateral = new FPVector3(-direction.Y, FP._0, direction.X) * LedgeLateralOffset;
            FPVector3 end = position + flat * distance;
            FP maxRise = FP._2;

            return IsStandable(f, end, position.Y, maxRise)
                && IsStandable(f, end + lateral, position.Y, maxRise)
                && IsStandable(f, end - lateral, position.Y, maxRise)
                && IsStandable(f, position + flat * (distance * FP._0_50), position.Y, maxRise);
        }

        public static void ClearPaths(BotBrain* brain)
        {
            brain->PathCount = 0;
            brain->PathCursor = 0;
        }

        // Chunks are min-corner pivoted and never rotated (LevelGenerationSystem.CommitPlacement).
        public static bool TryGetChunkCenter(Frame f, EntityRef chunkEntity, out FPVector3 center)
        {
            center = default;

            if (f.Unsafe.TryGetPointer<Chunk>(chunkEntity, out var chunk) == false
                || f.Unsafe.TryGetPointer<Transform3D>(chunkEntity, out var transform) == false)
            {
                return false;
            }

            center = transform->Position + new FPVector3(chunk->ChunkSizeWidth, FP._0, chunk->ChunkSizeDepth) * FP._0_50;
            return true;
        }

        // Chunks reachable by land from startChunk (BFS over Chunk.ConnectedChunks).
        public static HashSet<EntityRef> CollectReachableChunks(Frame f, EntityRef startChunk)
        {
            var visited = new HashSet<EntityRef> { startChunk };
            var queue = new Queue<EntityRef>();
            queue.Enqueue(startChunk);

            while (queue.Count > 0)
            {
                EntityRef current = queue.Dequeue();

                if (f.Unsafe.TryGetPointer<Chunk>(current, out var chunk) == false)
                    continue;

                for (int i = 0; i < chunk->ConnectedChunkCount; i++)
                {
                    EntityRef neighbor = chunk->ConnectedChunks[i];

                    if (visited.Add(neighbor) == true)
                        queue.Enqueue(neighbor);
                }
            }

            return visited;
        }

        // Null set (bot's own chunk unresolved) or an unresolvable candidate both read as reachable -
        // permissive rather than refusing to pick anything.
        public static bool IsPositionReachable(Frame f, HashSet<EntityRef> reachable, FPVector3 position)
        {
            if (reachable == null)
                return true;

            if (EnemyPathfindingUtility.TryFindContainingChunk(f, position, out EntityRef chunk) == false)
                return true;

            return reachable.Contains(chunk);
        }

        public static HashSet<EntityRef> CollectReachableFrom(Frame f, FPVector3 position)
        {
            return EnemyPathfindingUtility.TryFindContainingChunk(f, position, out EntityRef chunk) == true
                ? CollectReachableChunks(f, chunk)
                : null;
        }

        // Same KCC.Teleport idiom PlayerFallSystem uses - dropped slightly above so it settles onto
        // the ground, velocity zeroed because Teleport doesn't clear it.
        public static void Teleport(Frame f, ref BotInputSystem.Filter filter, FPVector3 position)
        {
            filter.KCC->Teleport(f, position + FPVector3.Up);
            filter.KCC->SetKinematicVelocity(FPVector3.Zero);
            filter.KCC->SetDynamicVelocity(FPVector3.Zero);
            filter.KCC->SetExternalImpulse(FPVector3.Zero);

            filter.Brain->Data.Direction = default;
            filter.Brain->Heading = default;
            ClearPaths(filter.Brain);
        }

        // Rotates a flat (X, Z) direction around world up - FPMath, deterministic.
        public static FPVector2 Rotate(FPVector2 direction, FP degrees)
        {
            FPMath.SinCos(degrees * FP.Deg2Rad, out FP sin, out FP cos);

            return new FPVector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
        }

        public static FP FlatDistance(FPVector3 a, FPVector3 b)
        {
            FPVector3 delta = b - a;
            delta.Y = FP._0;
            return delta.Magnitude;
        }
    }
}
