namespace Quantum
{
    using System;
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
        // The Boss entity lives on its own physics layer (not Enemy) so its collision matrix row
        // can turn off Player collision without affecting every other enemy - see
        // QuantumDefaultConfigs.asset's LayerMatrix. It still carries the plain Enemy ECS
        // component, so component-filtered (-1 mask) queries elsewhere already find it
        // automatically; only layer-mask-restricted queries need Boss added explicitly.
        private const string BossLayerName = "Boss";
        private const string IgnoreProjectileLayerName = "IgnoreProjectile";
        private const string ObstacleLayerName = "Obstacle";
        private const string GroundNotJumpableLayerName = "GroundNotJumpable";

        // No static caching for any of the masks/indices below - f.Layers.GetLayerMask/
        // GetLayerIndex are cheap lookups into immutable per-match config; a static field would
        // live outside Quantum's Frame/rollback state entirely.

        public static int GetPlayerLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(PlayerLayerName);
        }

        // Level geometry (walls, floor) lives on this layer - used to pin IsBlockedByWall/
        // TryFindGroundHeight's raycasts to exactly that instead of relying on -1 (every layer)
        // to happen to include it.
        public static int GetGroundLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(GroundLayerName);
        }

        // Unrelated to decoy targeting, which uses a plain Decoy-component scan instead (see
        // TryFindNearestDecoy). Includes Boss's own separate layer too (see BossLayerName above) -
        // every layer-mask-restricted "find an enemy" query in this codebase routes through this
        // one shared helper, so Boss only needed adding here once.
        public static int GetEnemyLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(EnemyLayerName) | f.Layers.GetLayerMask(BossLayerName);
        }

        // ProjectileSystem excludes this layer from its hit raycast so a projectile passes through
        // an entity on it instead of being consumed on contact for zero damage; the physics layer
        // matrix also excludes it from colliding with Enemy, so an entity on it physically passes
        // through enemy bodies too. DashSkillData moves the dasher onto it for the duration of a dash.
        public static int GetIgnoreProjectileLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(IgnoreProjectileLayerName);
        }

        // Level props/walls that aren't the Ground layer itself - used by GroupSpawnerUtility's
        // clearance overlap query (Player | Enemy | Obstacle) so a spawn candidate is rejected for
        // overlapping blocking geometry, without also rejecting the floor it needs to stand on.
        public static int GetObstacleLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(ObstacleLayerName);
        }

        // Everything a shot that hit nothing damageable is allowed to STOP on - real level
        // geometry, and nothing else. Needed because "is this a wall" cannot be answered by
        // EntityRef.None in this project (walls are genuinely dynamic entities, see
        // WeaponSystem.IsHitscanTarget) and cannot be answered by the Default layer either, which
        // Breakable props share with dropped orbs, chests, POI props and deployables. Deliberately
        // does NOT include Player/Enemy/Boss: those are resolved as real targets before anything
        // asks whether they block.
        public static int GetShotBlockerLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(GroundLayerName, GroundNotJumpableLayerName, ObstacleLayerName);
        }

        // Player | IgnoreProjectile - a dashing player sits on IgnoreProjectile for the dash's
        // duration (see DashSkillData.Begin/End), so a plain GetPlayerLayerMask query (what every
        // enemy-attack/targeting call site above deliberately relies on to give dash its i-frames)
        // can't see them. Anything FRIENDLY to that player wants the opposite - a pickup shouldn't be
        // missed because you dashed through it, and an ally buff shouldn't skip its own caster - so
        // those queries OR the two masks together via this instead of GetPlayerLayerMask.
        //
        // Named for the property rather than for pickups (it was GetPlayerIncludingDashingLayerMask): the exclusion it
        // undoes has nothing to do with pickups, and every friendly query hits it. Two Dash-END
        // Ascensions did exactly that (Brute's Bodyguard, Zara's Portable Speaker) - see
        // FindPlayersInRadiusIncludingDashing below.
        public static int GetPlayerIncludingDashingLayerMask(Frame f)
        {
            return GetPlayerLayerMask(f) | GetIgnoreProjectileLayerMask(f);
        }

        public static int GetIgnoreProjectileLayerIndex(Frame f)
        {
            return f.Layers.GetLayerIndex(IgnoreProjectileLayerName);
        }

        public static int GetPlayerLayerIndex(Frame f)
        {
            return f.Layers.GetLayerIndex(PlayerLayerName);
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

        // A true 3D sphere - correctly accounts for vertical distance too (Flying chase detection,
        // or a dash's hit-check regardless of height). Skips a Downed/KO player (see
        // docs/revive.md/PlayerLifeStateUtility.IsIncapacitated) the same way TryFindNearestEnemy
        // already skips a dying/Invulnerable enemy below - deliberately NOT a plain
        // f.Has<Invulnerable> check, since that tag is also used for two other still-Alive cases
        // (Max's Cheat Death, post-revive grace) that must stay targetable.
        //
        // Was a Physics3D.OverlapShape on the Player layer mask; now a direct scan of the entities
        // actually ON that layer (PlayerQueryUtility), which is the same set for a fraction of the
        // cost and none of the frame-heap allocation - see that class's own comment.
        public static bool TryFindNearestPlayer(Frame f, FPVector3 origin, FP range, out EntityRef entity)
        {
            return PlayerQueryUtility.TryFindNearestOnPlayerLayer(f, origin, range, GetPlayerLayerMask(f),
                skipIncapacitated: true, out entity);
        }

        // Reverse of TryFindNearestPlayer - for a non-enemy shooter (e.g. Lux's sentry gun) that
        // needs to find something on the Enemy layer to aim at. Mirrors AimSystem's own private
        // FindClosestTarget (used for player aim-assist): flat (XZ-only) distance so elevation
        // doesn't skew which one counts as closest, and skips a dying/lingering enemy
        // (EnemyActionPhase.Dead lasts DeathLingerTime for its death animation) or an Invulnerable
        // one (e.g. burrowed - see BurrowDeliveryData) the same way.
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

                if (f.Has<Invulnerable>(candidate) == true)
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

        // Same set as TryFindNearestPlayer but returns every player in range instead of just the
        // nearest one - for area deliveries (e.g. LeapDeliveryData's landing-zone damage) that need
        // to hit everyone caught in the blast, not a single target. Unlike TryFindNearestPlayer this
        // does NOT skip a Downed/KO player; every caller applies its own eligibility rule.
        //
        // Fills the caller's buffer (stackalloc it at PlayerQueryUtility.MaxPlayerLayerCandidates)
        // and returns how many were written - a span rather than the old
        // Physics3D.HitCollection3D return, which allocated on the frame heap on every single call.
        public static int FindPlayersInRadius(Frame f, FPVector3 origin, FP radius, Span<EntityRef> buffer)
        {
            return PlayerQueryUtility.GatherOnPlayerLayer(f, origin, radius, GetPlayerLayerMask(f), buffer);
        }

        // Same as FindPlayersInRadius but via GetPlayerIncludingDashingLayerMask, so a DASHING player
        // still shows up - see that mask's own comment for why every friendly query needs the broader
        // mask while everyone else (enemy attacks/targeting) keeps the narrower GetPlayerLayerMask one.
        //
        // Any friendly query that can coincide with a dash MUST use this, and one that fires AT dash
        // end always coincides with it: PhysicsSystem3D runs before every user system, so the
        // broadphase a Dash-End SkillAction queries was built while the dasher was still on
        // IgnoreProjectile - DashSkillData.End restoring the layer moments earlier in the same tick
        // comes too late to matter. A plain FindPlayersInRadius there silently drops the dasher every
        // single time, which is exactly how Brute's Bodyguard and Zara's Portable Speaker both ended up
        // never affecting their own caster.
        public static int FindPlayersInRadiusIncludingDashing(Frame f, FPVector3 origin, FP radius, Span<EntityRef> buffer)
        {
            return PlayerQueryUtility.GatherOnPlayerLayer(f, origin, radius, GetPlayerIncludingDashingLayerMask(f), buffer);
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
        // the shared write-site every movement profile funnels through, so gap/cliff handling
        // (below) applies uniformly regardless of which profile picked the direction.
        public static void MoveInDirection(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, FPVector2 direction, FP speed)
        {
            // A queued climb/gap hop waiting out its brief anticipation window owns this tick too -
            // checked before the in-flight hop below since a queued hop hasn't launched yet (see
            // QueueTraversalJump/TraversalJumpAnticipationTime).
            if (TickTraversalJumpAnticipation(f, ref filter, data) == true)
                return;

            // A traversal hop already in flight fully owns Transform3D.Position for its whole
            // duration (kinematic, not physics-driven) - skip everything else below until it lands.
            if (TickTraversalJump(f, ref filter, data) == true)
                return;

            if (direction.SqrMagnitude <= FP._0)
            {
                StopMovement(f, ref filter, data);
                return;
            }

            FPVector2 normalized = direction.Normalized;
            bool isGrounded = data.Stats.Height.InitialState == EnemyHeightState.Grounded;
            int groundLayerMask = GetGroundLayerMask(f);
            FP radius = ResolveEntityRadius(f, filter.Entity);

            if (data.Stats.AvoidWalls == true)
            {
                normalized = SteerAroundWalls(f, filter.Transform3D->Position, normalized, data.Stats.WallAvoidProbeDistance, radius, groundLayerMask);
            }

            FPVector3 flatDirection = new FPVector3(normalized.X, FP._0, normalized.Y);

            // Transform3D.Position is this entity's collider CENTER, not its feet - a sphere resting
            // on the floor has its center sitting a full radius above the ground (see IsGrounded's
            // own ResolveShapeHalfHeight use just below). Every traversal probe below adds a small
            // FIXED vertical offset (AnkleProbeHeight, cliffHeight, the edge-check's Up*0.1) meant to
            // be measured from the ground, not from wherever the collider's center happens to float -
            // groundPosition re-anchors X/Z at the same pivot but Y at the real ground surface
            // (IsGrounded's own groundY, already resolved for currentlyGrounded below, at zero extra
            // cost), so those offsets land where they're actually supposed to. Left un-corrected, a
            // wider-radius enemy's ankle probe reads well above true ankle height (missing/misjudging
            // short ledges) and its ground-ahead check starts high enough to sail clean over normal,
            // unbroken flat ground - misread as "no ground ahead" and spuriously hopping a gap that
            // was never actually there.
            // Pre-assigned (not left to IsGrounded's own out param) since the && below short-circuits
            // and never calls IsGrounded at all for a non-Grounded-type enemy - same harmless
            // "current Y" fallback IsGrounded documents for its own not-grounded case.
            FP groundY = filter.Transform3D->Position.Y;
            bool currentlyGrounded = isGrounded == true && IsGrounded(f, filter.Entity, filter.Transform3D->Position, groundLayerMask, out groundY) == true;
            FPVector3 groundPosition = new FPVector3(filter.Transform3D->Position.X, groundY, filter.Transform3D->Position.Z);

            // Every probe below is measured from this entity's own center pivot (X/Z; see
            // groundPosition above for why Y is re-anchored separately), so radius is added on top of
            // each one (plus EnemyHeightData.Climb/GapProbeThreshold's own extra clearance) -
            // otherwise a wide enemy's own body can still be overlapping the obstacle/edge at the
            // sampled point even though the ray from its center cleared it. Two separate distances,
            // not one shared value - a climbable ledge and a jumpable gap are different-enough
            // geometry questions (a short step up right in front of the body vs. clearing the body's
            // own edge to see past it) to want independently tuned margins.
            FP climbProbeDistance = radius + data.Stats.Height.ClimbProbeThreshold;
            FP gapProbeDistance = radius + data.Stats.Height.GapProbeThreshold;

            if (currentlyGrounded == true && data.Stats.Height.CanClimbCliffs == true &&
                TryFindClimbLanding(f, groundPosition, flatDirection, data.Stats.Height.CliffHeight, climbProbeDistance, radius, groundLayerMask, out FPVector3 climbLanding) == true)
            {
                // Climbs a blocking obstacle up to CliffHeight tall instead of walking into it -
                // mirrors the player's own auto-mantle (PlayerMovementProcessor.TryDetectMantle/
                // DoJump), just as a scripted kinematic hop (BeginTraversalJump) rather than a
                // physics launch - see that method's own comment for why. Queued rather than
                // launched immediately - see QueueTraversalJump/TraversalJumpAnticipationTime.
                QueueTraversalJump(f, ref filter, data, climbLanding, speed);
                return;
            }

            if (currentlyGrounded == true &&
                HasGroundAhead(f, groundPosition, flatDirection, gapProbeDistance, EdgeCheckDistance, groundLayerMask) == false)
            {
                if (data.Stats.Height.CanJumpGaps == true &&
                    TryFindGapLanding(f, groundPosition, flatDirection, gapProbeDistance, data.Stats.Height.GapDistance, GapScanStep, groundLayerMask, out FPVector3 gapLanding) == true)
                {
                    QueueTraversalJump(f, ref filter, data, gapLanding, speed);
                    return;
                }

                // CanFallFromCliff just means "don't stop here" - falls through to the normal
                // velocity assignment below with no special handling, so walking past the edge
                // falls naturally under gravity instead of hopping like the branch above does.
                bool canFall = data.Stats.Height.CanFallFromCliff == true &&
                    HasGroundWithinFallDistance(f, groundPosition, flatDirection, gapProbeDistance, data.Stats.Height.FallHeight, groundLayerMask) == true;

                if (canFall == false)
                {
                    StopMovement(f, ref filter, data);
                    return;
                }
            }

            FPVector3 desiredVelocity = flatDirection * speed;
            bool isFlying = data.Stats.Height.InitialState == EnemyHeightState.Flying;

            // Grounded/Airborne enemies only steer horizontally - vertical velocity is whatever
            // gravity already had it at, not overwritten here. Flying enemies hold a spring-eased
            // hover height instead - see ComputeFlyingHoverVelocity.
            filter.PhysicsBody3D->Velocity = isFlying == true
                ? new FPVector3(desiredVelocity.X, ComputeFlyingHoverVelocity(f, ref filter, data), desiredVelocity.Z)
                : new FPVector3(desiredVelocity.X, filter.PhysicsBody3D->Velocity.Y, desiredVelocity.Z);

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

        // Opt-in via EnemyStatsData.AvoidWalls - steers a chosen move direction along a wall
        // directly ahead instead of straight into it, resolved here a tick before
        // PhysicsSystem3D's own collision response would otherwise settle on roughly the same
        // result, so the enemy reads as routing around the obstacle rather than
        // stalling/juddering against it first. Sphere-cast, not a bare raycast - a centerline
        // ray can pass clean by a corner post that the enemy's own body (this same radius is
        // literally what EnemySystem.SeedRadius sizes PhysicsCollider3D to) would still clip,
        // which is exactly the "gets stuck in corners" failure a point probe has no way to
        // catch. Same ComputeDetailedInfo requirement as any query that reads Hit3D.Normal -
        // HitStatics|HitKinematics without it leaves Normal zeroed, which the SqrMagnitude
        // guard below treats as "no usable hit" and passes the original direction through
        // unchanged.
        public static FPVector2 SteerAroundWalls(Frame f, FPVector3 origin, FPVector2 direction, FP probeDistance, FP radius, int layerMask)
        {
            if (direction.SqrMagnitude <= FP._0 || probeDistance <= FP._0)
                return direction;

            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Y);
            Shape3D probeShape = Shape3D.CreateSphere(radius);
            Hit3D? hit = f.Physics3D.ShapeCast(origin, FPQuaternion.Identity, probeShape, flatDirection * probeDistance, layerMask,
                QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.ComputeDetailedInfo);

            if (hit.HasValue == false)
                return direction;

            FPVector2 normal = new FPVector2(hit.Value.Normal.X, hit.Value.Normal.Z);

            if (normal.SqrMagnitude <= FP._0)
                return direction;

            normal = normal.Normalized;

            FP into = FPVector2.Dot(direction, normal);

            // Already moving away from (or along) the wall - nothing to correct. Only a
            // direction that's actually heading into the surface (negative dot with the
            // outward normal) gets deflected.
            if (into >= FP._0)
                return direction;

            // Strip the into-wall component, keep only the tangential slide - the direction
            // that runs along the wall instead of through it.
            FPVector2 tangent = direction - normal * into;

            return tangent.SqrMagnitude > FP._0 ? tangent.Normalized : direction;
        }

        // Fixed geometry constants for the traversal probes below - deliberately not authored
        // per-enemy (see EnemyHeightData.CanClimbCliffs/CanJumpGaps/CanFallFromCliff's own
        // comments): the only per-enemy levers left are those three booleans, their three companion
        // heights/distances (CliffHeight/GapDistance/FallHeight), and Climb/GapProbeThreshold (the
        // clearance added to this enemy's own radius for the climb probe vs. the gap/fall probes
        // respectively) - BeginTraversalJump's kinematic lerp always reaches whichever landing point
        // these probes find, so one shared probe setup here is enough for every enemy that opts in.
        private static readonly FP AnkleProbeHeight = FP._0_25;
        // internal (not private) so SmartFleeMovementData's own HasGroundAhead/TryFindGapLanding
        // safety probes read the exact same numbers MoveInDirection's dead-end check below does -
        // otherwise a heading it judged "safe" could still disagree with what MoveInDirection
        // actually does with it a moment later.
        internal static readonly FP EdgeCheckDistance = 1;
        internal static readonly FP GapScanStep = FP._0_25;

        // Ankle-blocked/CliffHeight-clear dual-probe test, same shape as the player's own auto-mantle
        // (PlayerMovementProcessor.TryDetectMantle) - enemies aren't KCC entities, so this
        // re-implements the geometry check against f.Physics3D.Raycast instead of KCC's own wrapped
        // raycast. probeDistance (radius + threshold, resolved once by the caller - see
        // MoveInDirection) only sizes the ankle probe's own search reach - the ledge-height check and
        // landing sample below aim at the REAL obstacle this found (hit point + bodyRadius), not a
        // fixed distance from this enemy's own center, so the hop always lands a bit past the actual
        // ledge edge regardless of how close/far within that search reach it happened to be detected.
        // Returns the actual ground point on top of the obstacle (via TryFindGroundHeight), not just
        // a yes/no, so BeginTraversalJump has an exact destination to hop onto.
        public static bool TryFindClimbLanding(Frame f, FPVector3 position, FPVector3 direction, FP cliffHeight, FP probeDistance, FP bodyRadius, int groundLayerMask, out FPVector3 landingPoint)
        {
            landingPoint = default;

            if (direction.SqrMagnitude <= FP._0 || cliffHeight <= FP._0 || probeDistance <= FP._0)
                return false;

            FPVector3 normalizedDirection = direction.Normalized;
            QueryOptions queryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

            FPVector3 ankleOrigin = position + FPVector3.Up * AnkleProbeHeight;
            Hit3D? ankleHit = f.Physics3D.Raycast(ankleOrigin, normalizedDirection, probeDistance, groundLayerMask, queryOptions);

            if (ankleHit.HasValue == false)
                return false; // nothing ahead to climb - normal movement continues untouched

            // Aim at the obstacle actually found, not a fixed distance from this enemy's own center -
            // hit point + bodyRadius past it in the travel direction, so the hop clears the ledge's
            // real edge by one body-width regardless of how deep into the search reach that edge
            // happened to be. A wide enemy's own longer probeDistance no longer lets the ledge-height
            // check below reach past a short, genuinely climbable ledge onto unrelated geometry
            // further back the way a fixed-distance check would - it only ever probes/lands relative
            // to where the obstacle actually is.
            FP obstacleDepth = FPVector3.Distance(ankleOrigin, ankleHit.Value.Point);
            FP clearDistance = obstacleDepth + bodyRadius;

            FPVector3 ledgeOrigin = position + FPVector3.Up * cliffHeight;
            bool ledgeBlocked = f.Physics3D.Raycast(ledgeOrigin, normalizedDirection, clearDistance, groundLayerMask, queryOptions).HasValue;

            if (ledgeBlocked == true)
                return false; // taller than CliffHeight - just a wall, same as any other blocked path

            FPVector3 samplePosition = position + normalizedDirection * clearDistance;

            if (TryFindGroundHeight(f, samplePosition, groundLayerMask, out FP groundY) == false)
                return false;

            landingPoint = new FPVector3(samplePosition.X, groundY, samplePosition.Z);
            return true;
        }

        // Same "is there ground within reach ahead" check PlayerMovementProcessor.HasGroundAhead
        // uses to trigger the player's own auto-hop - enemies aren't KCC entities, so this
        // re-implements the same geometry test against f.Physics3D.Raycast instead of KCC's own
        // wrapped raycast. Used by MoveInDirection to detect a gap/cliff edge ahead - what happens
        // next (jump/fall/stop) is decided by the three EnemyHeightData flags, not by this probe.
        public static bool HasGroundAhead(Frame f, FPVector3 position, FPVector3 direction, FP probeDistance, FP checkDistance, int groundLayerMask)
        {
            if (direction.SqrMagnitude <= FP._0)
                return true; // not moving anywhere - nothing to check

            FPVector3 checkOrigin = position + direction.Normalized * probeDistance + FPVector3.Up * FP._0_10;
            Hit3D? hit = f.Physics3D.Raycast(checkOrigin, FPVector3.Down, checkDistance, groundLayerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);
            return hit.HasValue;
        }

        // Called only once HasGroundAhead has already failed at startDistance - scans further
        // out (in GapScanStep increments, up to maxDistance) for ground reappearing beyond the gap,
        // e.g. the far lip of a chasm/pit. Starts at startDistance (the near edge already confirmed
        // empty), not 0, so it can't re-detect the ground this enemy is currently standing on.
        // Returns the actual landing point (via TryFindGroundHeight) so BeginTraversalJump has an
        // exact destination to hop onto, not just a yes/no.
        public static bool TryFindGapLanding(Frame f, FPVector3 position, FPVector3 direction, FP startDistance, FP maxDistance, FP probeStep, int groundLayerMask, out FPVector3 landingPoint)
        {
            landingPoint = default;

            if (direction.SqrMagnitude <= FP._0 || probeStep <= FP._0)
                return false;

            FPVector3 normalizedDirection = direction.Normalized;

            for (FP distance = startDistance + probeStep; distance <= maxDistance; distance += probeStep)
            {
                FPVector3 samplePosition = position + normalizedDirection * distance;

                if (TryFindGroundHeight(f, samplePosition, groundLayerMask, out FP groundY) == true)
                {
                    landingPoint = new FPVector3(samplePosition.X, groundY, samplePosition.Z);
                    return true;
                }
            }

            return false;
        }

        // CanFallFromCliff only wants this enemy walking off an edge if the drop is short enough to
        // matter (FallHeight), not into a bottomless pit or off the bottom of the level. Probes
        // straight down from just past the edge HasGroundAhead already found empty, not from this
        // enemy's own current Y, so the measured drop is the real one at the landing spot rather than
        // wherever this enemy happens to already be standing.
        public static bool HasGroundWithinFallDistance(Frame f, FPVector3 position, FPVector3 direction, FP edgeDistance, FP maxFallDistance, int groundLayerMask)
        {
            if (direction.SqrMagnitude <= FP._0 || maxFallDistance <= FP._0)
                return false;

            FPVector3 edgePosition = position + direction.Normalized * edgeDistance;
            Hit3D? hit = f.Physics3D.Raycast(edgePosition, FPVector3.Down, maxFallDistance, groundLayerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);

            return hit.HasValue;
        }

        // Small windup between finding a climb/gap landing (QueueTraversalJump) and actually
        // launching the kinematic hop (BeginTraversalJump) - gives the view a real window to play a
        // crouch tell (EnemyBlobAnimationView) before the hop's own launch pop, instead of the two
        // overlapping on the same tick. Fixed/shared rather than authored per-enemy, same reasoning
        // as AnkleProbeHeight/EdgeCheckDistance/GapScanStep above - every enemy that climbs/jumps
        // gets the identical brief anticipation. Public so the view can read this same duration back
        // to normalize its own crouch progress, with no separately-authored copy to drift out of sync.
        public static readonly FP TraversalJumpAnticipationTime = FP._0_20;

        // Advances the brief windup before a queued hop launches - returns true while still waiting
        // (same "fully owns this tick" contract TickTraversalJump's own in-flight case uses) so the
        // enemy stands frozen playing its crouch tell instead of still sliding forward on stale
        // velocity. The instant the timer elapses, hands off to the real kinematic hop using
        // whatever destination/speed QueueTraversalJump captured when the anticipation began -
        // that same tick still counts as "handled" here, consistent with TickTraversalJump's own
        // landing tick.
        public static bool TickTraversalJumpAnticipation(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data)
        {
            if (filter.Enemy->TraversalJumpAnticipationTimer <= FP._0)
                return false;

            filter.Enemy->TraversalJumpAnticipationTimer -= f.DeltaTime;
            StopMovement(f, ref filter, data);

            if (filter.Enemy->TraversalJumpAnticipationTimer > FP._0)
                return true;

            BeginTraversalJump(f, ref filter, data, filter.Enemy->TraversalJumpPendingDestination, filter.Enemy->TraversalJumpPendingSpeed);
            return true;
        }

        // Advances a traversal hop already in flight (started by BeginTraversalJump) and returns
        // true while one is still running, so MoveInDirection can skip its own direction/speed
        // logic for the tick - the hop fully owns Transform3D.Position until it lands. Same
        // kinematic lerp-plus-parabola arc LeapDeliveryData.Tick already uses for a leap attack,
        // just driven from here instead of the EnemyActionPhase/EnemyDeliveryData pipeline (a
        // traversal hop happens mid-Chasing, not as its own action). TraversalJumpDuration <= 0 is
        // the sentinel for "no hop active".
        public static bool TickTraversalJump(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data)
        {
            if (filter.Enemy->TraversalJumpDuration <= FP._0)
                return false;

            filter.Enemy->TraversalJumpTimer += f.DeltaTime;
            FP t = FPMath.Clamp01(filter.Enemy->TraversalJumpTimer / filter.Enemy->TraversalJumpDuration);

            FPVector3 origin = filter.Enemy->TraversalJumpOrigin;
            FPVector3 destination = filter.Enemy->TraversalJumpDestination;
            FPVector3 flatPosition = FPVector3.Lerp(origin, destination, t);

            // Purely a visual arc bump ON TOP OF the lerp's own already-linear rise from origin.Y
            // to destination.Y - NOT the full climb height, which flatPosition.Y above already
            // covers. Using the full rise here (an earlier bug) double-counted it: the enemy would
            // rise by the climb height via the lerp AND by another full climb height via this
            // parabola, peaking a full CliffHeight above the actual landing before diving back down
            // to it - a lopsided, too-high-looking arc, worse the taller the climb. EnemyHeightData.
            // ArcHeight is a flat bump added on top regardless of rise, so it reads as a hop for
            // both a flat gap-jump and a climb alike - tune it per enemy, doesn't affect where or
            // when this actually lands.
            FP heightOffset = data.Stats.Height.ArcHeight * 4 * t * (FP._1 - t); // parabola, peaks at t=0.5, zero at t=0/1

            filter.Transform3D->Position = new FPVector3(flatPosition.X, flatPosition.Y + heightOffset, flatPosition.Z);

            // Editor-only visualization (Quantum's own deterministic Draw API, visible in the Scene
            // view) - redrawn every tick the hop is in flight, since each Draw call only paints the
            // current frame: the straight-line reference shows where flatPosition's lerp is headed,
            // the green sphere marks the exact landing point, the red sphere traces this tick's real
            // (arc-offset) position, so the whole hop's actual path is visible while it plays out.
            Draw.Line(origin, destination, ColorRGBA.Yellow);
            Draw.Sphere(destination, FP._0_25, ColorRGBA.Green);
            Draw.Sphere(filter.Transform3D->Position, FP._0_20, ColorRGBA.Red);

            FPVector3 delta = destination - origin;
            if (delta.SqrMagnitude > FP._0)
                filter.Aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;

            if (t < FP._1)
                return true;

            // Landed - snap exactly onto the captured spot (avoids any residual lerp drift) and
            // hand control back to normal physics/gravity.
            filter.Transform3D->Position = destination;
            filter.PhysicsBody3D->IsKinematic = false;
            filter.Enemy->TraversalJumpDuration = FP._0;

            // Larger, distinct blue sphere marking exactly where this hop actually settled, so a
            // landing that drifted from the green target sphere above is easy to spot at a glance.
            Draw.Sphere(destination, FP._0_50, ColorRGBA.Blue);

            Log.Info($"[TraversalJump] {filter.Entity} LANDED at={destination} elapsed={filter.Enemy->TraversalJumpTimer}");

            return true; // this tick was still spent landing, not steering - resumes normally next tick
        }

        // Beyond plain walking speed, a hop's horizontal speed also grows with sqrt(distance) -
        // see BeginTraversalJump's own comment for why.
        private static readonly FP JumpSpeedScale = FP._2;

        // Starts a kinematic hop from this enemy's current position onto destination (found by
        // TryFindClimbLanding/TryFindGapLanding) - duration scales with distance/speed so a slower
        // enemy takes proportionally longer to cross the same gap/cliff, same as it would walking
        // that distance. Unlike a physics launch, a scripted position lerp always reaches its exact
        // destination regardless of distance/speed, so CliffHeight/GapDistance stay the only levers
        // that decide whether a hop is even attempted - EnemyHeightData.TraversalJumpSpeedMultiplier
        // only paces how fast it plays out once it's already been found reachable. IsKinematic = true
        // for the hop's duration so PhysicsSystem3D's own gravity/collision response doesn't fight
        // the scripted position writes TickTraversalJump makes every tick - reset back to false
        // once TickTraversalJump reports landed.
        public static void BeginTraversalJump(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, FPVector3 destination, FP speed)
        {
            FPVector3 origin = filter.Transform3D->Position;

            // destination.Y is the raw ground-SURFACE height at the landing spot (from
            // TryFindClimbLanding/TryFindGapLanding's own TryFindGroundHeight call) - this entity's
            // own Transform3D.Position is its pivot, not necessarily its collider's bottom (e.g. a
            // capsule centered at chest height), so landing straight on that raw surface would
            // sink/float the entity relative to how it's already correctly resting at takeoff.
            // Measure that same pivot-to-ground offset here and reapply it at the landing spot -
            // same fix LeapDeliveryData.Begin already needed for its own jump arc.
            if (TryFindGroundHeight(f, origin, GetGroundLayerMask(f), out FP takeoffGroundY) == true)
            {
                destination.Y += origin.Y - takeoffGroundY;
            }

            FP distance = FPVector3.Distance(origin, destination);

            // Plain distance/speed (walking pace) makes duration grow LINEARLY with distance - fine
            // for a short climb hop, but a long gap-jump at the exact same walking speed drags out
            // into an unnatural slow-motion float, especially sitting right next to a climb's own
            // snappy short hop with the identical ArcHeight. Boosting speed by sqrt(distance) beyond
            // whatever walking alone provides keeps duration growing sub-linearly instead (duration
            // ~ distance/sqrt(distance) = sqrt(distance)): a short climb's own distance already sits
            // under this curve, so it's completely unaffected (still exactly as snappy as before);
            // only a longer gap gets progressively faster, so it reads as one energetic leap instead
            // of a wide, floaty glide.
            FP jumpSpeed = FPMath.Max(speed, FPMath.Sqrt(distance) * JumpSpeedScale);

            // <= 0 (every asset authored before this field existed included) reads as 1 - no change -
            // same convention Projectile.qtn's own MaxDistanceMultiplier already uses, so nothing
            // already in the game silently speeds up or stalls the instant this field exists.
            FP speedMultiplier = data.Stats.Height.TraversalJumpSpeedMultiplier <= FP._0 ? FP._1 : data.Stats.Height.TraversalJumpSpeedMultiplier;
            jumpSpeed *= speedMultiplier;

            FP duration = jumpSpeed > FP._0 ? FPMath.Max(FP._0_10, distance / jumpSpeed) : FP._0_50;

            filter.Enemy->TraversalJumpOrigin = origin;
            filter.Enemy->TraversalJumpDestination = destination;
            filter.Enemy->TraversalJumpTimer = FP._0;
            filter.Enemy->TraversalJumpDuration = duration;
            filter.PhysicsBody3D->IsKinematic = true;

            Log.Info($"[TraversalJump] {filter.Entity} BEGIN origin={origin} destination={destination} distance={distance} speed={speed} speedMultiplier={speedMultiplier} duration={duration}");
        }

        // Starts the brief TraversalJumpAnticipationTime windup instead of launching the hop on the
        // spot - called by MoveInDirection the instant TryFindClimbLanding/TryFindGapLanding finds a
        // landing, with TickTraversalJumpAnticipation handing off to the real BeginTraversalJump once
        // it elapses. destination/speed are captured now (not re-resolved when the hop actually
        // launches) so a target moving during the brief freeze below can't retarget an already-found
        // landing. Faces the enemy toward it right away, since nothing else drives Aim.Angle while
        // frozen (StopMovement doesn't touch it), so the crouch tell at least reads facing the right
        // way instead of holding whatever direction it was last walking.
        public static void QueueTraversalJump(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, FPVector3 destination, FP speed)
        {
            filter.Enemy->TraversalJumpAnticipationTimer = TraversalJumpAnticipationTime;
            filter.Enemy->TraversalJumpPendingDestination = destination;
            filter.Enemy->TraversalJumpPendingSpeed = speed;

            FaceTarget(filter.Aim, filter.Transform3D->Position, destination);
            StopMovement(f, ref filter, data);
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

        // ignoreEntity skips one specific entity's own collider - for a caller asking "what is the
        // ground UNDER this thing", which is never the thing itself. Without it anything that both
        // sits on the Ground layer and grounds itself (a level chunk carrying a GroundOffset) reads
        // its own floor as the surface to rest on and climbs itself, one clearance per tick, forever.
        public static bool TryFindGroundHeight(Frame f, FPVector3 position, int layerMask, out FP groundY, EntityRef ignoreEntity = default)
        {
            FPVector3 origin = new FPVector3(position.X, position.Y + GroundRaycastHeight, position.Z);
            var hits = f.Physics3D.RaycastAll(origin, FPVector3.Down, GroundRaycastHeight * 2, layerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics);
            hits.Sort(origin);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity != EntityRef.None && (hitEntity == ignoreEntity || f.Has<Enemy>(hitEntity) == true || f.Has<PlayerLink>(hitEntity) == true))
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

        // EnemyActionData.IgnoreY's own doc comment already promises this ("captured target/anchor
        // points use the enemy's own ground Y instead of the target's raw Y") but nothing in the
        // simulation actually implemented it - only EnemyAttackVisualsView's telegraph rendering
        // read the flag, cosmetically, for the decal shown to the player. That let a delivery like
        // FanProjectileDeliveryData fire pellets straight at the target's real (possibly very
        // different) elevation while its own paired telegraph rendered as a flat ground cone,
        // producing a spread that visually diverges from its own warning indicator - worse the more
        // pellets fan out, since every pellet inherits the same wrong elevation at once (Y-axis
        // rotation can't change a vector's tilt from vertical). Applied once here, at every
        // Enemy.SkillTargetPosition capture site (EnemySystem.UpdateChasing/UpdateActive,
        // EnemyDeliveryData.OnAnticipating), so every delivery's Begin()/Tick() sees an
        // already-flattened value with no per-delivery IgnoreY check needed - a Flying enemy (or any
        // action authored with IgnoreY = false) opts out and keeps tracking real height unchanged.
        public static FPVector3 ResolveIgnoreY(FPVector3 selfPosition, FPVector3 targetPosition, bool ignoreY)
        {
            if (ignoreY == false)
                return targetPosition;

            targetPosition.Y = selfPosition.Y;
            return targetPosition;
        }

        public static FP FlatSqrDistance(FPVector3 a, FPVector3 b)
        {
            FP dx = a.X - b.X;
            FP dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }

        // Actual floor height under a world point (via TryFindGroundHeight - the same top-down
        // ground raycast used elsewhere for boss/enemy re-grounding), not the raw Y a Transform3D
        // happens to carry - a capsule's own pivot/height offset, or exactly where a grenade's arc
        // landed, doesn't necessarily match which floor an entity is actually standing on. Falls
        // back to the position's own Y if no ground is found beneath it (e.g. over a pit) rather
        // than failing outright.
        public static FP ResolveGroundY(Frame f, FPVector3 position)
        {
            return TryFindGroundHeight(f, position, GetGroundLayerMask(f), out FP groundY) ? groundY : position.Y;
        }

        // Ground-area delivery gate shared by ranged blasts (AreaHitData/HitEffectUtility.
        // ApplyInRadius) and melee/instant ground slams (GroundAreaDeliveryData) - flat (XZ)
        // distance against radius, plus an explicit vertical gate comparing each side's own ACTUAL
        // FLOOR height (see ResolveGroundY), so a target standing on a genuinely different,
        // elevated/lowered platform isn't caught by a ground-level attack even when the raw
        // Transform3D.Y values happen to read close. centerGroundY is the caller's own
        // ResolveGroundY(f, center) result, resolved once per blast/slam rather than re-raycast per
        // candidate.
        public static bool IsWithinFlatGroundArea(Frame f, FPVector3 center, FP centerGroundY, FPVector3 targetPosition, FP radius, FP maxHeightDifference)
        {
            FP targetGroundY = ResolveGroundY(f, targetPosition);

            if (FPMath.Abs(targetGroundY - centerGroundY) > maxHeightDifference)
                return false;

            return FlatSqrDistance(center, targetPosition) <= radius * radius;
        }
    }
}
