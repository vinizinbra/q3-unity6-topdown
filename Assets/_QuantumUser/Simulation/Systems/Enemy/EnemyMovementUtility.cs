namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared by EnemySystem (its own Idle/Chasing/Recovery logic) and every EnemyDeliveryData
    // subclass (Begin/Tick implementations), so delivery types don't need to reach back into the
    // system that owns them to move the enemy, query for the player, or resolve target positions -
    // the same "shared static utility" shape as DamageUtility/ProjectileSpawner elsewhere in this project.
    public static unsafe class EnemyMovementUtility
    {
        private const string PlayerLayerName = "Player";
        private const string GroundLayerName = "Ground";
        private const string EnemyLayerName = "Enemy";
        private const string IgnoreProjectileLayerName = "IgnoreProjectile";
        private const string ObstacleLayerName = "Obstacle";

        private static int? _playerLayerMask;
        private static int? _groundLayerMask;
        private static int? _enemyLayerMask;
        private static int? _ignoreProjectileLayerMask;
        private static int? _obstacleLayerMask;
        private static int? _playerLayerIndex;
        private static int? _ignoreProjectileLayerIndex;

        public static int GetPlayerLayerMask(Frame f)
        {
            _playerLayerMask ??= f.Layers.GetLayerMask(PlayerLayerName);
            return _playerLayerMask.Value;
        }

        // Level geometry (walls, floor) lives on this layer - used to pin IsBlockedByWall/
        // TryFindGroundHeight's raycasts to exactly that instead of relying on -1 (every layer)
        // to happen to include it.
        public static int GetGroundLayerMask(Frame f)
        {
            _groundLayerMask ??= f.Layers.GetLayerMask(GroundLayerName);
            return _groundLayerMask.Value;
        }

        // Unrelated to decoy targeting, which uses a plain Decoy-component scan instead (see
        // TryFindNearestDecoy).
        public static int GetEnemyLayerMask(Frame f)
        {
            _enemyLayerMask ??= f.Layers.GetLayerMask(EnemyLayerName);
            return _enemyLayerMask.Value;
        }

        // ProjectileSystem excludes this layer from its hit raycast so a projectile passes through
        // an entity on it instead of being consumed on contact for zero damage. DashSkillData moves
        // the dasher onto it for the duration of a dash.
        public static int GetIgnoreProjectileLayerMask(Frame f)
        {
            _ignoreProjectileLayerMask ??= f.Layers.GetLayerMask(IgnoreProjectileLayerName);
            return _ignoreProjectileLayerMask.Value;
        }

        // Level props/walls that aren't the Ground layer itself - used by GroupSpawnerUtility's
        // clearance overlap query (Player | Enemy | Obstacle) so a spawn candidate is rejected for
        // overlapping blocking geometry, without also rejecting the floor it needs to stand on.
        public static int GetObstacleLayerMask(Frame f)
        {
            _obstacleLayerMask ??= f.Layers.GetLayerMask(ObstacleLayerName);
            return _obstacleLayerMask.Value;
        }

        public static int GetIgnoreProjectileLayerIndex(Frame f)
        {
            _ignoreProjectileLayerIndex ??= f.Layers.GetLayerIndex(IgnoreProjectileLayerName);
            return _ignoreProjectileLayerIndex.Value;
        }

        public static int GetPlayerLayerIndex(Frame f)
        {
            _playerLayerIndex ??= f.Layers.GetLayerIndex(PlayerLayerName);
            return _playerLayerIndex.Value;
        }

        public static bool TryGetTargetPosition(Frame f, EntityRef target, out FPVector3 position)
        {
            if (target == EntityRef.None || f.Exists(target) == false || f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
            {
                position = default;
                return false;
            }

            position = transform->Position;
            return true;
        }

        // Uses Physics3D.OverlapShape (broadphase query, needs PlayerLink entities on the
        // "Player" physics layer) so cost scales with nearby entities instead of total player
        // count. A true 3D sphere - correctly accounts for vertical distance too (Flying chase
        // detection, or a dash's hit-check regardless of height).
        public static bool TryFindNearestPlayer(Frame f, FPVector3 origin, FP range, out EntityRef entity)
        {
            Shape3D sphere = Shape3D.CreateSphere(range);
            var hits = f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, sphere, GetPlayerLayerMask(f), QueryOptions.HitAll);

            if (hits.Count == 0)
            {
                entity = EntityRef.None;
                return false;
            }

            hits.Sort(origin);
            entity = hits[0].Entity;
            return true;
        }

        // Reverse of TryFindNearestPlayer - for a non-enemy shooter (e.g. Lux's sentry gun) that
        // needs to find something on the Enemy layer to aim at. Mirrors AimSystem's own private
        // FindClosestTarget (used for player aim-assist): flat (XZ-only) distance so elevation
        // doesn't skew which one counts as closest, and skips a dying/lingering enemy
        // (EnemyActionPhase.Dead lasts DeathLingerTime for its death animation) the same way.
        public static bool TryFindNearestEnemy(Frame f, FPVector3 origin, FP range, out EntityRef entity)
        {
            Shape3D sphere = Shape3D.CreateSphere(range);
            var hits = f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, sphere, GetEnemyLayerMask(f), QueryOptions.HitAll);

            entity = EntityRef.None;
            FP closestFlatSqrDistance = FP._0;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == true && enemy->Phase == EnemyActionPhase.Dead)
                    continue;

                FP flatSqrDistance = FlatSqrDistance(origin, transform->Position);

                if (entity == EntityRef.None || flatSqrDistance < closestFlatSqrDistance)
                {
                    entity = candidate;
                    closestFlatSqrDistance = flatSqrDistance;
                }
            }

            return entity != EntityRef.None;
        }

        // Same query as TryFindNearestPlayer but returns every player in range instead of just
        // the nearest one - for area deliveries (e.g. LeapDeliveryData's landing-zone damage) that
        // need to hit everyone caught in the blast, not a single target.
        public static Physics3D.HitCollection3D FindPlayersInRadius(Frame f, FPVector3 origin, FP radius)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            return f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, sphere, GetPlayerLayerMask(f), QueryOptions.HitAll);
        }

        // "Max aggro": a Decoy always wins over the nearest player, regardless of distance. A
        // plain f.Filter<Decoy, Transform3D>() linear scan rather than a physics-layer query -
        // decoys are sparse (at most one per player, short-lived), and a layer-based query would
        // conflate with GetPlayerLayerMask (Decoy sits on the Player layer so enemy attack
        // hit-connect checks actually land on it - see Decoy.qtn), degrading "decoy always wins"
        // into "decoy is just another nearest-wins candidate".
        public static bool TryFindNearestDecoy(Frame f, FPVector3 origin, FP range, out EntityRef entity)
        {
            entity = EntityRef.None;
            FP rangeSqr = range * range;
            bool found = false;
            FP closestSqr = default;

            var filtered = f.Filter<Decoy, Transform3D>();
            while (filtered.Next(out EntityRef candidate, out Decoy _, out Transform3D transform))
            {
                FP sqrDistance = FlatSqrDistance(origin, transform.Position);

                if (sqrDistance > rangeSqr)
                    continue;

                if (found == false || sqrDistance < closestSqr)
                {
                    found = true;
                    closestSqr = sqrDistance;
                    entity = candidate;
                }
            }

            return found;
        }

        // Flying enemies chase/charge FlightHeight above the target's actual position; Grounded
        // enemies target it directly. Shared by chase movement and by any attack (e.g. Charge)
        // that captures a destination point once and moves toward it over several ticks.
        public static FPVector3 ResolveDestination(EnemyDataAsset data, FPVector3 targetPosition)
        {
            if (data.Stats.Height.InitialState == EnemyHeightState.Flying)
            {
                targetPosition.Y += data.Stats.Height.FlightHeight;
            }

            return targetPosition;
        }

        // Drives PhysicsBody3D.Velocity from an EnemyMovementData-computed ground-plane direction
        // (see EnemyMovementData.ComputeMoveDirection) rather than an absolute destination point -
        // the shared write-site every movement profile funnels through, so ledge-avoidance
        // (below) applies uniformly regardless of which profile picked the direction.
        public static void MoveInDirection(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, FPVector2 direction, FP speed)
        {
            if (direction.SqrMagnitude <= FP._0)
            {
                StopMovement(f, ref filter, data);
                return;
            }

            FPVector2 normalized = direction.Normalized;
            FPVector3 flatDirection = new FPVector3(normalized.X, FP._0, normalized.Y);
            bool isGrounded = data.Stats.Height.InitialState == EnemyHeightState.Grounded;
            int groundLayerMask = GetGroundLayerMask(f);

            // Carried through untouched by default so PhysicsSystem3D's own gravity integration
            // isn't overwritten - only replaced below if this tick triggers a hop.
            FP verticalVelocity = filter.PhysicsBody3D->Velocity.Y;

            // Actual current contact state - distinct from the `isGrounded` category flag above
            // (which just says this enemy TYPE is Grounded, not Flying/Airborne, and stays true for
            // its whole lifetime including mid-jump). Both branches below need to know whether this
            // entity is touching ground RIGHT NOW: the jump branch to avoid re-triggering/stacking
            // hops before landing, and the AvoidLedges branch so it doesn't fire mid-air - without
            // this gate, the ledge-ahead probe below ran every tick of a jump's arc too, and its
            // origin (this entity's own, now-elevated, position) could easily miss the obstacle's
            // top surface and read as "no ground ahead", freezing the jump mid-air via StopMovement.
            bool currentlyGrounded = isGrounded == true && IsGrounded(f, filter.Entity, filter.Transform3D->Position, groundLayerMask) == true;

            FPVector3 desiredVelocity = flatDirection * speed;

            if (currentlyGrounded == true && data.Stats.Height.CanJump == true && data.Stats.Height.CanCrossObstacles == true &&
                CanCrossLedge(f, filter.Transform3D->Position, flatDirection, data.Stats.Height.AnkleProbeHeight, data.Stats.Height.MaxLedgeHeight, data.Stats.Height.MantleProbeDistance, groundLayerMask) == true)
            {
                // Hops over a low obstacle instead of walking into it - mirrors the player's own
                // auto-mantle (PlayerMovementProcessor.TryDetectMantle/DoJump). Gated on already
                // being grounded so an enemy mid-hop (no longer grounded until it lands) can't
                // re-trigger and stack jumps before gravity brings it back down. Captures this
                // tick's horizontal velocity into Enemy.JumpHorizontalVelocity, reasserted below
                // for the rest of the arc.
                verticalVelocity = data.Stats.Height.JumpVelocity;
                filter.Enemy->JumpHorizontalVelocity = desiredVelocity;
            }
            else if (currentlyGrounded == true && data.Stats.Height.AvoidLedges == true &&
                HasGroundAhead(f, filter.Transform3D->Position, flatDirection, data.Stats.Height.EdgeProbeDistance, data.Stats.Height.EdgeCheckDistance, groundLayerMask) == false)
            {
                StopMovement(f, ref filter, data);
                return;
            }

            bool isFlying = data.Stats.Height.InitialState == EnemyHeightState.Flying;

            // A Grounded enemy currently mid-air (e.g. still arcing through the hop triggered
            // above) keeps reasserting the horizontal velocity it launched with
            // (JumpHorizontalVelocity), actively overriding PhysicsBody3D.Drag - which grounded
            // movement never feels, since this function overwrites velocity every tick while
            // grounded - and ignoring the AI's own steering, instead of decelerating/re-steering
            // mid-air as if it still had footing. Only .Y (gravity/the hop itself) changes until
            // IsGrounded is true again. Doesn't apply to Airborne-state enemies - isGrounded is
            // already false for them structurally, not because they're mid-jump (no real consumer
            // yet, see EnemyHeightState's own comment) - or Flying, which both always steer
            // horizontally same as before.
            bool keepJumpVelocity = isFlying == false && isGrounded == true && currentlyGrounded == false;

            if (keepJumpVelocity == true)
            {
                FPVector3 jumpVelocity = filter.Enemy->JumpHorizontalVelocity;
                filter.PhysicsBody3D->Velocity = new FPVector3(jumpVelocity.X, verticalVelocity, jumpVelocity.Z);
            }
            else
            {
                // Grounded/Airborne enemies only steer horizontally - vertical velocity is whatever
                // gravity/the hop above produced, not overwritten here. Flying enemies hold a
                // spring-eased hover height instead - see ComputeFlyingHoverVelocity.
                filter.PhysicsBody3D->Velocity = isFlying == true
                    ? new FPVector3(desiredVelocity.X, ComputeFlyingHoverVelocity(f, ref filter, data), desiredVelocity.Z)
                    : new FPVector3(desiredVelocity.X, verticalVelocity, desiredVelocity.Z);
            }

            filter.Aim->Angle = FPMath.Atan2(normalized.X, normalized.Y) * FP.Rad2Deg;
        }

        // Only meaningful for EnemyHeightState.Flying - called from MoveInDirection and
        // StopMovement so hover-hold applies whether the enemy is actively steering or standing
        // still. Periodically (EnemyHeightData.HoverCheckInterval) re-samples the ground directly
        // beneath this enemy via TryFindGroundHeight and remembers groundY + FlightHeight as
        // Enemy.FlyingHoverTargetHeight, then every tick eases the entity's own vertical velocity
        // toward that remembered target with a damped spring (HoverSpringFrequency/
        // HoverSpringDamping) instead of snapping straight to it - the "floaty" feel comes from
        // that spring, and re-checking the ground every tick instead of periodically would just
        // move the target out from under the spring mid-correction rather than letting it settle.
        // FlyingHoverCheckTimer/FlyingHoverTargetHeight both default to 0, so the very first tick
        // this runs always triggers an immediate check rather than hovering toward a bogus 0 target
        // for a whole HoverCheckInterval first.
        public static FP ComputeFlyingHoverVelocity(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data)
        {
            FP dt = f.DeltaTime;
            filter.Enemy->FlyingHoverCheckTimer -= dt;

            if (filter.Enemy->FlyingHoverCheckTimer <= FP._0)
            {
                filter.Enemy->FlyingHoverCheckTimer = data.Stats.Height.HoverCheckInterval;

                if (TryFindGroundHeight(f, filter.Transform3D->Position, GetGroundLayerMask(f), out FP groundY) == true)
                    filter.Enemy->FlyingHoverTargetHeight = groundY + data.Stats.Height.FlightHeight;
            }

            FP omega = data.Stats.Height.HoverSpringFrequency * FP.PiTimes2;
            FP displacement = filter.Transform3D->Position.Y - filter.Enemy->FlyingHoverTargetHeight;
            FP currentVelocityY = filter.PhysicsBody3D->Velocity.Y;
            FP force = -omega * omega * displacement - FP._2 * data.Stats.Height.HoverSpringDamping * omega * currentVelocityY;

            return currentVelocityY + force * dt;
        }

        // For attacks that need guaranteed, collision-response-immune movement (e.g. a charge that
        // shouldn't be slowed/deflected the instant it bumps into the target) - directly advances
        // Transform3D.Position toward destination at speed * deltaTime instead of writing Velocity
        // for PhysicsSystem3D to integrate. Pair with PhysicsBody3D.IsKinematic = true while using
        // this; EnemySystem.EnterRecovering resets IsKinematic back to false for every attack once
        // it finishes, so callers don't need to restore it themselves.
        public static void MoveKinematicTowards(ref EnemySystem.Filter filter, EnemyDataAsset data, FPVector3 selfPosition, FPVector3 destination, FP speed, FP deltaTime)
        {
            bool isFlying = data.Stats.Height.InitialState == EnemyHeightState.Flying;

            FPVector3 delta = isFlying == true
                ? destination - selfPosition
                : new FPVector3(destination.X - selfPosition.X, FP._0, destination.Z - selfPosition.Z);

            if (delta.SqrMagnitude <= FP._0)
                return;

            FP distance = delta.Magnitude;
            FP step = speed * deltaTime;

            filter.Transform3D->Position = step >= distance ? selfPosition + delta : selfPosition + delta.Normalized * step;
            filter.Aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
        }

        // For kinematic movement (MoveKinematicTowards) that directly writes Transform3D.Position
        // instead of Velocity, bypassing PhysicsSystem3D's normal collision response entirely -
        // this is the explicit substitute, used by ChargeDeliveryData.Tick to stop a charge at a
        // wall instead of clipping straight through it. QueryOptions.HitStatics only: a charging
        // enemy should stop on non-moving level geometry, never on the player it's trying to hit
        // or on another (possibly also-kinematic, mid-attack) enemy. layerMask pins the query to
        // GetGroundLayerMask (level geometry) rather than every layer, so it can't ever register
        // a false hit against something like a projectile or trigger volume that isn't a wall.
        public static bool IsBlockedByWall(Frame f, FPVector3 origin, FPVector3 direction, FP distance, int layerMask)
        {
            if (direction.SqrMagnitude <= FP._0 || distance <= FP._0)
                return false;

            // HitStatics | HitKinematics, not HitStatics alone - same combination
            // PlayerMovementProcessor already uses for its own ground/mantle checks. HitStatics
            // alone let DashSkillData's own wall check (originally written the same way) pass
            // straight through level-chunk wall geometry undetected.
            Hit3D? hit = f.Physics3D.Raycast(origin, direction.Normalized, distance, layerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);
            return hit.HasValue;
        }

        // Same ankle-blocked/ledge-clear dual-probe test PlayerMovementProcessor.TryDetectMantle
        // uses for the player's own auto-mantle - enemies aren't KCC entities (no KCCContext/
        // KCCShapeCastInfo available to them, only plain PhysicsBody3D), so this re-implements the
        // same geometry check against f.Physics3D.Raycast instead of KCC's own wrapped raycast.
        // The algorithm is what's shared between player and enemy traversal, not the literal call.
        // EnemyHeightData.CanCrossObstacles gates whether a caller should even ask; this only
        // answers "is there a climbable ledge right here". Called from
        // EnemyMovementUtility.MoveInDirection when CanJump && CanCrossObstacles are both set.
        public static bool CanCrossLedge(Frame f, FPVector3 position, FPVector3 direction, FP ankleProbeHeight, FP ledgeHeight, FP probeDistance, int layerMask)
        {
            if (direction.SqrMagnitude <= FP._0 || probeDistance <= FP._0)
                return false;

            FPVector3 normalizedDirection = direction.Normalized;
            QueryOptions queryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

            FPVector3 ankleOrigin = position + FPVector3.Up * ankleProbeHeight;
            bool ankleBlocked = f.Physics3D.Raycast(ankleOrigin, normalizedDirection, probeDistance, layerMask, queryOptions).HasValue;

            if (ankleBlocked == false)
                return false;

            FPVector3 ledgeOrigin = position + FPVector3.Up * ledgeHeight;
            bool ledgeBlocked = f.Physics3D.Raycast(ledgeOrigin, normalizedDirection, probeDistance, layerMask, queryOptions).HasValue;

            return ledgeBlocked == false;
        }

        // Same "is there ground within reach ahead" check PlayerMovementProcessor.HasGroundAhead
        // uses to trigger the player's own auto-hop - enemies aren't KCC entities, so this
        // re-implements the same geometry test against f.Physics3D.Raycast instead of KCC's own
        // wrapped raycast. Used by MoveInDirection to stop an EnemyHeightData.AvoidLedges enemy at
        // the edge rather than walking off it - no jump-across behavior, just a refusal to step
        // further in that direction.
        public static bool HasGroundAhead(Frame f, FPVector3 position, FPVector3 direction, FP probeDistance, FP checkDistance, int groundLayerMask)
        {
            if (direction.SqrMagnitude <= FP._0)
                return true; // not moving anywhere - nothing to check

            FPVector3 checkOrigin = position + direction.Normalized * probeDistance + FPVector3.Up * FP._0_10;
            Hit3D? hit = f.Physics3D.Raycast(checkOrigin, FPVector3.Down, checkDistance, groundLayerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);
            return hit.HasValue;
        }

        // Downward raycast from well above the given XZ to find the actual ground height there -
        // for a delivery that locks a landing spot from a captured target position (e.g.
        // LeapDeliveryData) rather than a live physics-integrated destination. A target's
        // Transform3D.Position is its own pivot, not necessarily ground level, and the ground
        // height there can differ from the enemy's own takeoff spot anyway (slopes, platforms) -
        // so a Grounded enemy needs the real terrain height, not a borrowed/assumed Y.
        // HitStatics | HitKinematics | HitDynamics, not HitStatics alone - same reasoning as
        // IsGrounded just below: some level geometry in this project is a genuinely dynamic entity,
        // not kinematic, so HitStatics alone silently misses it. RaycastAll (not the single-hit
        // Raycast) so a creature standing anywhere along the ray - not just resting exactly at
        // position - can be skipped in favor of the real ground underneath it, same
        // EntityRef.None-isn't-enough gotcha IsGrounded's own hitEntity check documents.
        private const int GroundRaycastHeight = 20;

        public static bool TryFindGroundHeight(Frame f, FPVector3 position, int layerMask, out FP groundY)
        {
            FPVector3 origin = new FPVector3(position.X, position.Y + GroundRaycastHeight, position.Z);
            var hits = f.Physics3D.RaycastAll(origin, FPVector3.Down, GroundRaycastHeight * 2, layerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics);
            hits.Sort(origin);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity != EntityRef.None && (f.Has<Enemy>(hitEntity) == true || f.Has<PlayerLink>(hitEntity) == true))
                    continue;

                groundY = hits[i].Point.Y;
                return true;
            }

            groundY = default;
            return false;
        }

        // Short downward probe to confirm actual ground contact - as opposed to "not currently
        // staggered," which says nothing about whether physics has actually brought a knocked-back
        // enemy back down yet (see EnemySystem.TickKnockbackRecovery). HitStatics | HitKinematics,
        // not HitStatics alone - same combination IsBlockedByWall already uses, since HitStatics
        // alone lets level-chunk geometry pass through undetected (see IsBlockedByWall's own comment).
        private static readonly FP GroundContactTolerance = FP._0_20 + FP._0_20;

        // position is the entity's own pivot, not necessarily its collider's bottom (e.g. a capsule
        // centered at chest height) - probing the fixed GroundContactTolerance alone would then never
        // reach the actual ground even while resting on it. Extending the probe by the collider's own
        // half-height first (same per-shape math ProjectileSystem.ResolveShapeHalfHeight uses) fixes
        // that; entity is optional purely so a caller without one handy still gets the flat-tolerance
        // behavior instead of a hard failure.
        public static bool IsGrounded(Frame f, EntityRef entity, FPVector3 position, int layerMask)
        {
            return IsGrounded(f, entity, position, layerMask, out _);
        }

        // Overload for a caller that also needs where the ground actually is (e.g.
        // JuggernautLandingImpactSystem.CorrectPosition, which needs the real hit point - not just
        // this entity's own current, possibly slightly-penetrating, Y - to work out how deep into the
        // surface the collider currently sits). groundY is this entity's own current Y when not
        // grounded, so a caller that forgets to check the bool back still gets something harmless.
        public static bool IsGrounded(Frame f, EntityRef entity, FPVector3 position, int layerMask, out FP groundY)
        {
            FP probeDistance = GroundContactTolerance;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                probeDistance += ResolveShapeHalfHeight(collider->Shape);
            }

            // HitDynamics included too (not just Statics|Kinematics) - some level geometry in this
            // project is a genuinely dynamic entity, not kinematic (see the Enemy/PlayerLink filter
            // right below for why that's safe to include: a real dynamic creature standing nearby
            // gets excluded, but real dynamic ground doesn't). Without this, an enemy resting on that
            // kind of geometry would never read as grounded at all.
            Hit3D? hit = f.Physics3D.Raycast(position, FPVector3.Down, probeDistance, layerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics);

            if (hit.HasValue == false)
            {
                groundY = position.Y;
                return false;
            }

            // HitKinematics/HitDynamics also match a creature (e.g. another enemy, kinematic while
            // Rooted or just a regular dynamic body otherwise), not just real level geometry -
            // resting on top of one isn't "grounded" any more than resting on top of a player would
            // be. Same gotcha as everywhere else in this codebase that reads a physics hit's Entity:
            // EntityRef.None only means static geometry, but a populated Entity can still be a
            // creature rather than a wall/floor.
            EntityRef hitEntity = hit.Value.Entity;

            if (hitEntity != EntityRef.None && (f.Has<Enemy>(hitEntity) == true || f.Has<PlayerLink>(hitEntity) == true))
            {
                groundY = position.Y;
                return false;
            }

            groundY = hit.Value.Point.Y;
            return true;
        }

        // Public so callers outside this file that need the same pivot-to-collider-bottom offset
        // (e.g. JuggernautLandingImpactSystem.CorrectPosition, computing how much of a grounded
        // collider currently sits below the ground hit point) don't need their own copy - mirrors
        // ProjectileSystem's own identical private copy, kept separate there for the same reason
        // AreaHitData/VortexSystem etc. don't share a base class: no shared System/AssetObject
        // ancestor to hang a common helper off of.
        public static FP ResolveShapeHalfHeight(Shape3D shape)
        {
            switch (shape.Type)
            {
                case Shape3DType.Sphere: return shape.Sphere.Radius;
                case Shape3DType.Box: return shape.Box.Extents.Y;
                case Shape3DType.Capsule: return shape.Capsule.Extent + shape.Capsule.Radius;
                default: return FP._0;
            }
        }

        // Actual collider radius (XZ footprint), as opposed to ResolveShapeHalfHeight's Y-axis
        // half-height - used where a caller wants "how wide is this entity" (e.g. scaling a VFX to
        // cover it) instead of an authored EnemyDataAsset value, which can drift from the real
        // collider or simply not be set. Box has no true radius, so it falls back to the wider of
        // its two horizontal extents.
        public static FP ResolveShapeRadius(Shape3D shape)
        {
            switch (shape.Type)
            {
                case Shape3DType.Sphere: return shape.Sphere.Radius;
                case Shape3DType.Capsule: return shape.Capsule.Radius;
                case Shape3DType.Box: return FPMath.Max(shape.Box.Extents.X, shape.Box.Extents.Z);
                default: return FP._0;
            }
        }

        // ResolveShapeRadius, resolved straight off whatever entity is actually asking - for
        // callers that don't already have a PhysicsCollider3D* in hand (unlike
        // JuggernautLandingImpactSystem.Filter.Collider, which can call ResolveShapeRadius
        // directly). 0 if the entity has no collider at all.
        public static FP ResolveEntityRadius(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true
                ? ResolveShapeRadius(collider->Shape)
                : FP._0;
        }

        // Position + collider Centroid offset (Shape3D.Centroid - "offset position of the shape
        // center related to the entity's position") - falls back to bare Transform3D.Position if
        // the entity has no collider. Same shape as ResolveEntityRadius; pulled out here so any
        // view component positioning a VFX at an entity's real visual center (not its feet/origin)
        // can share it instead of re-deriving it (see EnemyAllyLinkView/EnemyStatusEffectsView).
        public static FPVector3 ResolveEntityCenter(Frame f, EntityRef entity)
        {
            FPVector3 center = f.Get<Transform3D>(entity).Position;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
                center += collider->Shape.Centroid;

            return center;
        }

        // Random point in a ring around anchor - a ring, not a filled disc, so minDistance > 0 keeps
        // the result off the anchor itself (e.g. so a spawned entity doesn't land inside whatever's
        // standing on the anchor). Same polar-offset idiom VortexSystem.Update already uses for its
        // own random mini-explosion position, pulled out here so any delivery that wants a
        // scattered-around-a-point result (not just ScatterDeliveryData) can share it.
        public static FPVector3 RandomPositionInRing(Frame f, FPVector3 anchor, FP minDistance, FP maxDistance)
        {
            FP angle = f.RNG->Next(0, 360);
            FP distance = f.RNG->Next(minDistance, maxDistance);
            FPVector3 offset = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward * distance;

            return anchor + offset;
        }

        // Takes Frame/Filter (not just data+body like before) so a stationary Flying enemy still
        // hover-holds via ComputeFlyingHoverVelocity instead of freezing dead in the air the moment
        // it stops steering - previously this zeroed Flying's vertical velocity outright.
        public static void StopMovement(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data)
        {
            filter.PhysicsBody3D->Velocity = data.Stats.Height.InitialState == EnemyHeightState.Flying
                ? new FPVector3(FP._0, ComputeFlyingHoverVelocity(f, ref filter, data), FP._0)
                : new FPVector3(FP._0, filter.PhysicsBody3D->Velocity.Y, FP._0);
        }

        public static void FaceTarget(Aim* aim, FPVector3 selfPosition, FPVector3 targetPosition)
        {
            FPVector3 delta = new FPVector3(targetPosition.X - selfPosition.X, FP._0, targetPosition.Z - selfPosition.Z);

            if (delta.SqrMagnitude <= FP._0)
                return;

            aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
        }

        public static FP FlatSqrDistance(FPVector3 a, FPVector3 b)
        {
            FP dx = a.X - b.X;
            FP dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }
    }
}
