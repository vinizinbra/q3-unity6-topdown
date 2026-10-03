namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Serialization;

    // Where BurrowDeliveryData resurfaces relative to the target. TowardTarget scatters around the
    // target's own live position (the original ambush behavior - RandomizeAroundAnchor's ring keeps
    // it off the target exactly, not on top of them). AwayFromTarget instead anchors FleeDistance
    // out along the same flee direction FleeMovementData already uses (self minus target, XZ-only),
    // then applies that same ring scatter on top of THAT point - a burrow that runs from the player
    // rather than at them, reusing the ring math either way rather than a second scatter mechanism.
    public enum BurrowRelocationDirection { TowardTarget, AwayFromTarget }

    // Dives underground near its current spot, travels invisibly to a new point (scattered around
    // the target via the base class's RandomizeAroundAnchor), then resurfaces there - the enemy is
    // Invulnerable + Burrowed for the whole Active phase, so DamageUtility ignores every hit and
    // AimSystem/VortexSystem/EnemyMovementUtility.TryFindNearestEnemy all skip it as a target (see
    // their own Invulnerable checks). No damage by default - pure repositioning, same as
    // TeleportBlinkDeliveryData; whatever action the enemy commits to after resurfacing (via the
    // normal Recovery -> Chasing -> Preparation cycle) is its own separately-telegraphed, avoidable
    // attack. AttackOnResurface opts a specific instance into being the attack itself instead (see
    // its own comment).
    //
    // Pair with an EnemyActionData authored with a large EngageRange (TrySelectAction's range gate
    // can't be bypassed by Trigger alone - see EnemyDecisionUtility.cs), a long CooldownTime (so it
    // can't burrow back-to-back), and optionally Trigger.Type = OnHealthThreshold so it reads as an
    // escape rather than a random reposition.
    public unsafe class BurrowDeliveryData : EnemyDeliveryData
    {
        private const string BurrowedLayerName = "DeadEnemy";

        public FP DiveDuration = FP._0_50;

        // Underground move speed (units/second). Travel is real movement toward the destination at
        // this speed every tick - not a timed interpolation - so changing the destination mid-way
        // (RetargetAtPercent) just steers it, never snaps it. Formerly TravelSpeed.
        [FormerlySerializedAs("TravelSpeed")]
        public FP BurrowMoveSpeed = 5;

        // Fallback only when BurrowMoveSpeed <= 0: the speed is then derived once at Begin() as
        // distance / TravelDuration, so the first leg takes this long.
        public FP TravelDuration = FP._1;

        // Hard cap on time spent traveling underground (<= 0 = no cap). Needed once the destination
        // can keep moving - DirectionTracking = UpdateTargetDirectionWhileActive re-points it at the
        // live target every tick, and a target as fast as BurrowMoveSpeed is never caught. On timeout
        // it resurfaces right where it is, provided there's ground there (otherwise it keeps going
        // until there is, rather than popping up over a void).
        public FP MaxTravelDuration = 3;

        // Travel ends this far (flat) short of the destination, on the side it approached from - so a
        // homing burrow (UpdateTargetDirectionWhileActive) surfaces beside the target instead of
        // directly beneath it. 0 = lands exactly on the destination.
        public FP ArriveDistance;

        public FP ResurfaceDuration = FP._0_50;

        // How far below its own ground level the enemy sinks while Traveling - purely a visual/feel
        // parameter (the whole point is it's not visible then), doesn't affect where it lands.
        public FP DiveDepth = 2;

        public BurrowRelocationDirection RelocationDirection = BurrowRelocationDirection.TowardTarget;

        // Only consulted when RelocationDirection == AwayFromTarget - see that enum's own comment.
        public FP FleeDistance = 6;

        // A candidate landing spot is rejected (and re-rolled - see MaxLandingAttempts) unless
        // ground is also found this far out in every cardinal direction from it, not just at the
        // exact point itself - otherwise a spot right at a cliff/gap/level edge would still pass
        // (TryFindGroundHeight only samples the one point), landing the enemy right at the boundary
        // or, worse, having it fall straight off the level the instant it resurfaces. Matters most
        // for AwayFromTarget (a flee can easily push the destination toward the level boundary) but
        // applies to both directions - <= 0 disables the extra probes, checking only the exact point.
        public FP MinDistanceFromEdge = 2;

        // How many times to re-roll a candidate landing spot before giving up - see
        // ResolveDestination's own fallback.
        private const int MaxLandingAttempts = 5;

        // Last-resort ring ResolveDestination widens into once the author's own MinRandomOffset/
        // MaxRandomOffset scatter is exhausted without finding a clear spot - not author-exposed,
        // this only exists to guarantee genuine variation between attempts (see ResolveDestination).
        private static readonly FP FallbackScatterMin = 1;
        private static readonly FP FallbackScatterMax = 4;

        // Re-samples the anchor (ResolveAnchor - the target's LIVE position, or a freshly-recomputed
        // flee point) exactly ONCE, after this fraction (0-1) of the first leg's estimated travel time
        // (distance / speed at Begin), and steers toward a fresh scattered destination from there.
        // Speed-based travel means this never snaps the enemy. <= 0 (default) never retargets.
        // Only ever fires underground, mid-Travel.
        public FP RetargetAtPercent;

        // True: the instant it finishes resurfacing, hits every player within action.DamageRange of
        // the resurface point with action.Effects/Damage - a ground-burst "burrow attack" (surface
        // under the player and erupt) instead of pure repositioning. False (default): no damage at
        // all, matching this delivery's original behavior - action.Effects/Damage go unused.
        public bool AttackOnResurface;

        private FP ResolveMoveSpeed(FPVector3 start, FPVector3 destination)
        {
            if (BurrowMoveSpeed > FP._0)
                return BurrowMoveSpeed;

            FP distance = FlatDistance(start, destination);
            return TravelDuration > FP._0 && distance > FP._0 ? distance / TravelDuration : FP._1 * 1000;
        }

        private static FP FlatDistance(FPVector3 a, FPVector3 b)
        {
            FPVector3 delta = b - a;
            delta.Y = FP._0;
            return delta.Magnitude;
        }

        // targetPosition is passed in rather than read off Enemy.SkillTargetPosition: Begin() overwrites
        // that field with the chosen DESTINATION, so a mid-travel retarget reading it back would
        // scatter around the previous landing spot instead of the target (two stacked scatters -
        // read as the burrow surfacing at random spots).
        private FPVector3 ResolveAnchor(ref EnemySystem.Filter filter, FPVector3 targetPosition)
        {
            if (RelocationDirection == BurrowRelocationDirection.TowardTarget)
                return targetPosition;

            // Same flee-direction idiom FleeMovementData.ComputeMoveDirection uses (self minus
            // target, XZ-only) - anchored FleeDistance out so the ring scatter below still lands
            // generally away from the target instead of centered back on it.
            FPVector3 selfPosition = filter.Transform3D->Position;
            FPVector3 delta = new FPVector3(selfPosition.X - targetPosition.X, FP._0, selfPosition.Z - targetPosition.Z);
            FPVector3 fleeDirection = delta.SqrMagnitude > FP._0 ? delta.Normalized : FPVector3.Forward;

            return selfPosition + fleeDirection * FleeDistance;
        }

        // True only if `candidate` itself has ground beneath it AND (when MinDistanceFromEdge > 0) at
        // least HALF of the 4 cardinal probes that far out also find ground - see MinDistanceFromEdge's
        // own comment for why the single-point check alone isn't enough. Deliberately majority, not
        // unanimous: a narrow-but-genuinely-safe walkway (a bridge, a causeway) is legitimately close
        // to open air/water on its short axis and would fail EVERY probe on that side, rejecting
        // perfectly good ground purely for being narrow. Requiring only 2-of-4 still catches the case
        // this exists for (landing right at an actual cliff/void edge, where 3-4 directions come up
        // empty) without penalizing a straight bridge/corridor, which keeps ground ahead/behind along
        // its own length even when both side probes land in the water/void flanking it.
        private bool IsClearLandingSpot(Frame f, FPVector3 candidate, int groundLayerMask)
        {
            if (EnemyMovementUtility.TryFindGroundHeight(f, candidate, groundLayerMask, out _) == false)
                return false;

            if (MinDistanceFromEdge <= FP._0)
                return true;

            int clearCount = 0;

            for (int i = 0; i < 4; i++)
            {
                FPVector3 probe = candidate + FPQuaternion.Euler(0, i * 90, 0) * FPVector3.Forward * MinDistanceFromEdge;

                if (EnemyMovementUtility.TryFindGroundHeight(f, probe, groundLayerMask, out _) == true)
                    clearCount++;
            }

            return clearCount >= 2;
        }

        // Samples PathSampleCount evenly-spaced points along the straight line from `from` to `to` -
        // the exact same line Tick()'s Travel branch lerps X/Z along - and requires ground to exist
        // (ANY height - this only cares that ground exists there at all, never comparing heights
        // across samples, since height genuinely isn't a problem for something traveling
        // underground) at every one of them. Catches a start/destination pair that are each
        // individually fine (IsClearLandingSpot passes both) but whose straight travel path between
        // them crosses a real void/gap partway through - e.g. two separate platforms/islands that
        // are each solid ground but have nothing connecting them underground either.
        private const int PathSampleCount = 6;

        private static bool IsPathClear(Frame f, FPVector3 from, FPVector3 to, int groundLayerMask)
        {
            for (int i = 0; i <= PathSampleCount; i++)
            {
                FP t = (FP)i / PathSampleCount;
                FPVector3 point = FPVector3.Lerp(from, to, t);

                if (EnemyMovementUtility.TryFindGroundHeight(f, point, groundLayerMask, out _) == false)
                    return false;
            }

            return true;
        }

        // Re-rolls a fresh anchor/scatter (ResolveAnchor + RandomizeAroundAnchor) up to
        // MaxLandingAttempts times looking for one IsClearLandingSpot accepts, instead of trusting
        // the first roll the way the original single-shot ground check did - a raw scattered point
        // can easily land over a pit or right at the level boundary, especially for AwayFromTarget
        // (a flee can push straight toward the edge). If every attempt fails, falls back to the
        // enemy's OWN current position (guaranteed valid - it's already standing there) rather than
        // an unresolved point, so a bad roll reads as "dive and pop back up in place" instead of the
        // enemy vanishing into a void. Non-Grounded enemies (Flying) skip the ground requirement
        // entirely, same as the original behavior.
        private FPVector3 ResolveDestination(Frame f, EnemyDataAsset data, ref EnemySystem.Filter filter, FP fallbackY, FPVector3 targetPosition)
        {
            if (data.Stats.Height.InitialState != EnemyHeightState.Grounded)
            {
                FPVector3 raw = RandomizeAroundAnchor(f, ResolveAnchor(ref filter, targetPosition));
                return new FPVector3(raw.X, fallbackY, raw.Z);
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 anchor = ResolveAnchor(ref filter, targetPosition);

            // The straight-line-from-here check (IsPathClear) uses the enemy's LIVE position, not
            // just its Begin()-time start - correct for both call sites: at Begin() this IS the
            // start; from the RetargetAtPercent branch mid-Travel, this is wherever it currently is
            // (X/Z meaningful even though Y is currently sunk - IsPathClear doesn't care about
            // height), so a retarget only has to validate the REMAINING path, not the whole original
            // one.
            FPVector3 selfPosition = filter.Transform3D->Position;

            for (int attempt = 0; attempt < MaxLandingAttempts; attempt++)
            {
                // The author's own MinRandomOffset/MaxRandomOffset scatter - if that's 0 (an exact-
                // landing author choice, e.g. TowardTarget with no scatter authored), every one of
                // these MaxLandingAttempts is the IDENTICAL point, so this loop alone achieves
                // nothing on its own when the one exact spot is blocked - see the fallback ring below.
                FPVector3 raw = RandomizeAroundAnchor(f, anchor);

                if (IsClearLandingSpot(f, raw, groundLayerMask) == true &&
                    IsPathClear(f, selfPosition, raw, groundLayerMask) == true &&
                    EnemyMovementUtility.TryFindGroundHeight(f, raw, groundLayerMask, out FP groundY) == true)
                {
                    return new FPVector3(raw.X, groundY, raw.Z);
                }
            }

            // The author's own scatter (or lack of it) couldn't find a clear spot - widen the search
            // with a real, always-varied ring around the same anchor, independent of
            // MinRandomOffset/MaxRandomOffset (which may be 0 by design and would otherwise just
            // retry the identical blocked point forever) before finally giving up.
            for (int attempt = 0; attempt < MaxLandingAttempts; attempt++)
            {
                FPVector3 raw = EnemyMovementUtility.RandomPositionInRing(f, anchor, FallbackScatterMin, FallbackScatterMax);

                if (IsClearLandingSpot(f, raw, groundLayerMask) == true &&
                    IsPathClear(f, selfPosition, raw, groundLayerMask) == true &&
                    EnemyMovementUtility.TryFindGroundHeight(f, raw, groundLayerMask, out FP groundY) == true)
                {
                    return new FPVector3(raw.X, groundY, raw.Z);
                }
            }

            Log.Debug($"[Enemy] {filter.Entity} burrow found no clear landing spot near {anchor} - resurfacing in place");
            return new FPVector3(selfPosition.X, fallbackY, selfPosition.Z);
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->SkillStartPosition = filter.Transform3D->Position;

            // SkillTargetPosition is the target's position locked during the windup. ResolveAnchor
            // picks TowardTarget (that position) or AwayFromTarget (a flee point derived from it);
            // either way it's scattered and re-rolled until clear - see ResolveDestination. From here
            // on SkillTargetPosition holds the DESTINATION, not the target.
            FPVector3 destination = ResolveDestination(f, data, ref filter, filter.Enemy->SkillStartPosition.Y, filter.Enemy->SkillTargetPosition);

            filter.Enemy->SkillTargetPosition = destination;
            filter.Enemy->StateTimer = DiveDuration;
            filter.PhysicsBody3D->IsKinematic = true;

            f.Add<Invulnerable>(filter.Entity);
            f.Add<Burrowed>(filter.Entity);

            if (f.Unsafe.TryGetPointer<Burrowed>(filter.Entity, out var burrowed) == true)
            {
                FP speed = ResolveMoveSpeed(filter.Enemy->SkillStartPosition, destination);
                burrowed->Stage = StageDive;
                burrowed->MoveSpeed = speed;
                burrowed->TravelElapsed = FP._0;
                burrowed->RetargetAt = RetargetAtPercent * FlatDistance(filter.Enemy->SkillStartPosition, destination) / speed;
                burrowed->Retargeted = false;

                // Off the Enemy layer while underground - otherwise the kinematic body still shoves
                // other enemies and players aside along its travel path (DeadEnemy collides with
                // Ground/Obstacle only, and enemy queries use the Enemy|Boss mask so it stays
                // untargetable). Restored on resurface.
                if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(filter.Entity, out var collider) == true)
                {
                    burrowed->PreviousLayer = (byte)collider->Layer;
                    collider->Layer = (byte)f.Layers.GetLayerIndex(BurrowedLayerName);
                }
            }

            return false;
        }

        // Landing height comes from the real ground under it, not from the destination's own Y - with
        // UpdateTargetDirectionWhileActive the destination is the raw target position (and with
        // IgnoreY, the enemy's own sunk height), which would end the rise underground.
        private void BeginResurface(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, Burrowed* burrowed, FPVector3 landing)
        {
            if (EnemyMovementUtility.TryFindGroundHeight(f, landing, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY) == true)
                landing.Y = groundY;

            filter.Enemy->SkillStartPosition = landing;
            filter.Transform3D->Position = new FPVector3(landing.X, landing.Y - DiveDepth, landing.Z);
            burrowed->Stage = StageResurface;
            filter.Enemy->StateTimer = ResurfaceDuration;

            // A damaging eruption gets the same ground warning a Mortar shell does, for the whole rise -
            // the only tell of where it comes up. Owner-bound, so it clears if the enemy is interrupted.
            if (AttackOnResurface == true && ResurfaceDuration > FP._0 && action.DamageRange > FP._0)
                f.Events.ProjectileLandingWarning(landing, ResurfaceDuration, action.DamageRange, filter.Entity);
        }

        private const byte StageDive = 0;
        private const byte StageTravel = 1;
        private const byte StageResurface = 2;

        // Dive (timed sink in place) -> Travel (moves underground at BurrowMoveSpeed until it reaches
        // the destination) -> Resurface (timed rise). Dive/Resurface run off Enemy.StateTimer; Travel
        // has no duration at all, it simply ends on arrival.
        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<Burrowed>(filter.Entity, out var burrowed) == false)
                return true;

            // Same Void Pressure (Kai) time-dilation reasoning as LeapDeliveryData.Tick - only the
            // Active phase is stretched, not the windup.
            FP dt = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            FPVector3 start = filter.Enemy->SkillStartPosition;
            FPVector3 destination = filter.Enemy->SkillTargetPosition;

            if (burrowed->Stage == StageDive)
            {
                filter.Enemy->StateTimer -= dt;
                FP t = DiveDuration > FP._0 ? FPMath.Clamp01(FP._1 - filter.Enemy->StateTimer / DiveDuration) : FP._1;
                filter.Transform3D->Position = new FPVector3(start.X, start.Y - DiveDepth * t, start.Z);

                if (filter.Enemy->StateTimer <= FP._0)
                    burrowed->Stage = StageTravel;

                return false;
            }

            if (burrowed->Stage == StageTravel)
            {
                burrowed->TravelElapsed += dt;

                // Once, underground: steer toward a fresh spot around the target's LIVE position (see
                // ResolveAnchor). No live target (it died/left) keeps the original destination.
                if (RetargetAtPercent > FP._0 && burrowed->Retargeted == false &&
                    burrowed->TravelElapsed >= burrowed->RetargetAt &&
                    f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
                {
                    burrowed->Retargeted = true;
                    destination = ResolveDestination(f, data, ref filter, filter.Transform3D->Position.Y + DiveDepth, targetTransform->Position);
                    filter.Enemy->SkillTargetPosition = destination;
                }

                // Real movement: step flat toward the destination at MoveSpeed, stopping ArriveDistance
                // short of it. Height is irrelevant while hidden - BeginResurface snaps the landing
                // point to the real ground.
                FPVector3 current = filter.Transform3D->Position;
                FPVector3 toDestination = new FPVector3(destination.X - current.X, FP._0, destination.Z - current.Z);
                FP flatDistance = toDestination.Magnitude;
                FP remaining = flatDistance - ArriveDistance;
                FP step = burrowed->MoveSpeed * dt;
                FPVector3 direction = flatDistance > FP._0 ? toDestination / flatDistance : FPVector3.Zero;

                if (remaining > step)
                {
                    filter.Transform3D->Position = current + direction * step;

                    if (MaxTravelDuration <= FP._0 || burrowed->TravelElapsed < MaxTravelDuration)
                        return false;

                    // Out of time - surface here if there's ground, else keep going (see MaxTravelDuration).
                    // Probed from surface height - it's DiveDepth underground right now, and a ray
                    // starting inside the ground would never report it.
                    FPVector3 here = filter.Transform3D->Position + new FPVector3(FP._0, DiveDepth, FP._0);
                    if (EnemyMovementUtility.TryFindGroundHeight(f, here, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY) == false)
                        return false;

                    BeginResurface(f, ref filter, action, burrowed, new FPVector3(here.X, groundY, here.Z));
                    return false;
                }

                // Arrived (or already inside ArriveDistance): land ArriveDistance short, on the approach side.
                FPVector3 landing = current + direction * FPMath.Max(remaining, FP._0);
                BeginResurface(f, ref filter, action, burrowed, new FPVector3(landing.X, destination.Y, landing.Z));
                return false;
            }

            // Resurfacing - at the landing point locked by BeginResurface (SkillStartPosition, free
            // after the Dive), rising from -DiveDepth back to real ground level. Not read off
            // SkillTargetPosition: with UpdateTargetDirectionWhileActive that keeps following the
            // target, which would slide the enemy sideways while it rises.
            destination = filter.Enemy->SkillStartPosition;
            filter.Enemy->StateTimer -= dt;
            FP rise = ResurfaceDuration > FP._0 ? FPMath.Clamp01(FP._1 - filter.Enemy->StateTimer / ResurfaceDuration) : FP._1;
            filter.Transform3D->Position = new FPVector3(destination.X, destination.Y - DiveDepth * (FP._1 - rise), destination.Z);

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            filter.Transform3D->Position = destination;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(filter.Entity, out var collider) == true)
            {
                collider->Layer = burrowed->PreviousLayer;
            }

            f.Remove<Invulnerable>(filter.Entity);
            f.Remove<Burrowed>(filter.Entity);
            if (AttackOnResurface == true)
            {
                // Same FindPlayersInRadius + HitEffectUtility.ApplyToTarget idiom
                // GroundAreaDeliveryData.Begin uses for its own instant area hit - a ground-burst
                // erupting at the resurface point, radially outward same as a slam.
                Span<EntityRef> hits = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
                int hitsCount = EnemyMovementUtility.FindPlayersInRadius(f, destination, action.DamageRange, hits);

                for (int i = 0; i < hitsCount; i++)
                {
                    EntityRef hitEntity = hits[i];

                    if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                        continue;

                    FPVector3 hitPosition = hitTransform->Position;

                    HitEffectContext context = new HitEffectContext
                    {
                        Owner = filter.Entity,
                        Target = hitEntity,
                        Position = hitPosition,
                        PushDirection = hitPosition - destination,
                        Damage = action.Damage,
                        Source = DamageSource.None,
                        Element = ElementType.Neutral,
                    };

                    HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
                }
            }

            return true;
        }
    }
}
