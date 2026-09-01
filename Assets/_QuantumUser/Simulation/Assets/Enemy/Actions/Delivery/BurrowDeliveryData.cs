namespace Quantum
{
    using System;
    using Photon.Deterministic;

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
        public FP DiveDuration = FP._0_50;

        // Fixed Travel time, used only when TravelSpeed <= 0 (the default - every existing
        // BurrowDeliveryData asset keeps its exact current behavior). See TravelSpeed.
        public FP TravelDuration = FP._1;

        // > 0 switches Travel from a fixed duration to distance / TravelSpeed, so a short hop and a
        // long cross-arena relocation take proportionally different times instead of both taking
        // TravelDuration regardless of how far they actually go. Only Travel scales this way - Dive/
        // Resurface are in-place vertical sink/rise, not distance-dependent.
        public FP TravelSpeed;

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

        // Re-samples the anchor (ResolveAnchor - the target's live position, or a freshly-recomputed
        // flee point) exactly ONCE, the tick elapsed crosses this fraction (0-1) of the total Dive+
        // Travel+Resurface duration, and re-scatters/re-grounds Enemy.SkillTargetPosition from it -
        // same "single checkpoint, not continuous tracking" idiom EnemyActionData.AimLockPercent uses
        // for the windup, just applied to the Active phase instead (AimLock/OnAnticipating stop being
        // consulted the instant Begin() runs, so they can't cover this on their own). <= 0 (default)
        // never retargets - every existing asset keeps its original one-shot-at-Begin destination.
        // Author it to land inside the Travel phase (between DiveDuration and DiveDuration+Travel, as
        // a fraction of the total) so the position snap this can cause happens while the enemy is
        // still fully hidden underground, not mid-Dive/Resurface where it's visible.
        public FP RetargetAtPercent;

        // True: the instant it finishes resurfacing, hits every player within action.DamageRange of
        // the resurface point with action.Effects/Damage - a ground-burst "burrow attack" (surface
        // under the player and erupt) instead of pure repositioning. False (default): no damage at
        // all, matching this delivery's original behavior - action.Effects/Damage go unused.
        public bool AttackOnResurface;

        private FP ResolveTravelDuration(FPVector3 start, FPVector3 destination)
        {
            if (TravelSpeed <= FP._0)
                return TravelDuration;

            return FPVector3.Distance(start, destination) / TravelSpeed;
        }

        private FPVector3 ResolveAnchor(ref EnemySystem.Filter filter)
        {
            if (RelocationDirection == BurrowRelocationDirection.TowardTarget)
                return filter.Enemy->SkillTargetPosition;

            // Same flee-direction idiom FleeMovementData.ComputeMoveDirection uses (self minus
            // target, XZ-only) - anchored FleeDistance out so the ring scatter below still lands
            // generally away from the target instead of centered back on it.
            FPVector3 selfPosition = filter.Transform3D->Position;
            FPVector3 targetPosition = filter.Enemy->SkillTargetPosition;
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
        private FPVector3 ResolveDestination(Frame f, EnemyDataAsset data, ref EnemySystem.Filter filter, FP fallbackY)
        {
            if (data.Stats.Height.InitialState != EnemyHeightState.Grounded)
            {
                FPVector3 raw = RandomizeAroundAnchor(f, ResolveAnchor(ref filter));
                return new FPVector3(raw.X, fallbackY, raw.Z);
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 anchor = ResolveAnchor(ref filter);

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

            return new FPVector3(selfPosition.X, fallbackY, selfPosition.Z);
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->SkillStartPosition = filter.Transform3D->Position;

            // SkillTargetPosition is already the target's raw position (established by
            // OnAnticipating/UpdateChasing). ResolveAnchor (called inside ResolveDestination) picks
            // TowardTarget (that raw position) or AwayFromTarget (a flee point derived from it);
            // either way the result is scattered via the base class's own randomization (same as
            // ScatterDeliveryData) and re-rolled until it clears IsClearLandingSpot, falling back to
            // the enemy's own current position if nothing valid turns up - see ResolveDestination.
            FPVector3 destination = ResolveDestination(f, data, ref filter, filter.Enemy->SkillStartPosition.Y);

            filter.Enemy->SkillTargetPosition = destination;
            filter.Enemy->StateTimer = DiveDuration + ResolveTravelDuration(filter.Enemy->SkillStartPosition, destination) + ResurfaceDuration;
            filter.PhysicsBody3D->IsKinematic = true;

            f.Add<Invulnerable>(filter.Entity);
            f.Add<Burrowed>(filter.Entity);

            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 start = filter.Enemy->SkillStartPosition;
            FPVector3 destination = filter.Enemy->SkillTargetPosition;
            FP travelDuration = ResolveTravelDuration(start, destination);
            FP totalDuration = DiveDuration + travelDuration + ResurfaceDuration;

            FP timerBeforeTick = filter.Enemy->StateTimer;
            FP elapsedBefore = FPMath.Clamp(totalDuration - timerBeforeTick, FP._0, totalDuration);

            // Same Void Pressure (Kai) time-dilation reasoning as LeapDeliveryData.Tick - only the
            // Active phase is stretched, not the windup.
            filter.Enemy->StateTimer = timerBeforeTick - f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            FP elapsed = FPMath.Clamp(totalDuration - filter.Enemy->StateTimer, FP._0, totalDuration);

            if (RetargetAtPercent > FP._0)
            {
                FP retargetElapsed = RetargetAtPercent * totalDuration;

                if (elapsedBefore < retargetElapsed && elapsed >= retargetElapsed)
                {
                    destination = ResolveDestination(f, data, ref filter, start.Y);
                    filter.Enemy->SkillTargetPosition = destination;

                    travelDuration = ResolveTravelDuration(start, destination);
                    totalDuration = DiveDuration + travelDuration + ResurfaceDuration;

                    // Re-anchors StateTimer so the elapsed-so-far fraction survives the total-
                    // duration change (Speed mode's travel time depends on distance, which just
                    // changed by retargeting) - without this, elapsed/StateTimer would otherwise
                    // jump discontinuously on the very next tick.
                    filter.Enemy->StateTimer = FPMath.Max(totalDuration - elapsed, FP._0);
                }
            }

            if (elapsed < DiveDuration)
            {
                // Diving - sinks straight down in place, hasn't started traveling yet.
                FP t = DiveDuration > FP._0 ? FPMath.Clamp01(elapsed / DiveDuration) : FP._1;
                filter.Transform3D->Position = new FPVector3(start.X, start.Y - DiveDepth * t, start.Z);
            }
            else if (elapsed < DiveDuration + travelDuration)
            {
                // Traveling underground - fully sunk, moving from the takeoff spot to the resolved
                // destination. Y is held at each point's own -DiveDepth rather than lerped, so a
                // takeoff/landing height difference doesn't read as tunneling at an angle (it's
                // invisible anyway, but Resurface below still needs a clean start point).
                FP t = travelDuration > FP._0 ? FPMath.Clamp01((elapsed - DiveDuration) / travelDuration) : FP._1;
                FPVector3 flat = FPVector3.Lerp(start, destination, t);
                FP depth = FPMath.Lerp(start.Y - DiveDepth, destination.Y - DiveDepth, t);
                filter.Transform3D->Position = new FPVector3(flat.X, depth, flat.Z);
            }
            else
            {
                // Resurfacing - already at destination XZ, rising from -DiveDepth back to real
                // ground level.
                FP t = ResurfaceDuration > FP._0
                    ? FPMath.Clamp01((elapsed - DiveDuration - travelDuration) / ResurfaceDuration)
                    : FP._1;
                filter.Transform3D->Position = new FPVector3(destination.X, destination.Y - DiveDepth * (FP._1 - t), destination.Z);
            }

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            filter.Transform3D->Position = destination;

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
