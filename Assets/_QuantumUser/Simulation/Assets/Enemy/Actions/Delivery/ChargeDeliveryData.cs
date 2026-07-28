namespace Quantum
{
    using Photon.Deterministic;

    // Locked-direction dash ("bull charge"): captures direction the instant the windup ends and
    // commits to a fixed-distance straight line - no re-homing mid-dash, so the player can dodge
    // after the commit point.
    //
    // Moves kinematically (PhysicsBody3D.IsKinematic, via EnemyMovementUtility.MoveKinematicTowards)
    // instead of through Velocity, so collision response doesn't slow/deflect it on contact
    // (EnemySystem.EnterRecovering resets IsKinematic once the action ends). Since that bypasses
    // PhysicsSystem3D's own collision response, Tick runs its own wall check
    // (EnemyMovementUtility.IsBlockedByWall) each step to avoid clipping through geometry.
    //
    // Pair with an EnemyActionData authored with EngageRange well beyond DamageRange (so the charge
    // has room to close the gap it triggered from) and DirectionTracking = DoNotUpdateTargetDirection
    // (so the telegraph reads as a fixed, fair warning) - see EnemyActionData.DirectionTracking.
    public unsafe class ChargeDeliveryData : EnemyDeliveryData
    {
        public FP DashSpeed = 15;

        // Tune directly (not derived from DashSpeed * DashDuration); should be >= the action's
        // EngageRange so the charge can cover the gap it triggered from.
        public FP DashDistance = 8;

        // Safety timeout in case the charge never arrives/hits (e.g. stuck on geometry).
        public FP DashDuration = 1;

        // False: charge continues past a hit instead of stopping early (still ends on arrival/
        // timeout/wall). Known limitation: no per-charge "already hit" tracking, so a target that
        // stays in Range across ticks can take damage more than once from the same charge.
        public bool StopOnHit = true;

        // Raycast origin height above ground - keeps the ray inside a wall collider's body
        // instead of skimming the wall/floor seam at ground level, which can graze past it
        // undetected.
        public FP WallCheckHeight = 1;

        // Minimum wall-raycast lookahead, regardless of how little ground this tick covers -
        // DashSpeed * DeltaTime alone is too short a ray and can miss a collider by a hair
        // (confirmed by testing - this was the actual cause of charges clipping through walls).
        public FP WallCheckDistance = FP._0_75;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Uses Enemy.SkillTargetPosition as already established by OnAnticipating, rather
            // than re-resolving the target itself - that would bypass a locked-during-windup
            // telegraph by re-aiming right when the charge starts.
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 resolvedTarget = EnemyMovementUtility.ResolveDestination(data, filter.Enemy->SkillTargetPosition);
            bool isFlying = data.Stats.Height.InitialState == EnemyHeightState.Flying;

            FPVector3 delta = isFlying == true
                ? resolvedTarget - origin
                : new FPVector3(resolvedTarget.X - origin.X, FP._0, resolvedTarget.Z - origin.Z);

            if (delta.SqrMagnitude <= FP._0)
                return true; // already on top of the target - nothing to charge toward

            filter.Enemy->SkillTargetPosition = origin + delta.Normalized * DashDistance;
            filter.Enemy->StateTimer = DashDuration;

            // Kinematic for the whole dash - also why OnInterrupted is never overridden here:
            // DamageUtility.ApplyResolvedImpulse skips a kinematic PhysicsBody3D entirely, so a
            // knockback can never actually reach this delivery while Active regardless of
            // EnemyActionData.InterruptibleDuringActive.
            filter.PhysicsBody3D->IsKinematic = true;

            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 selfPosition = filter.Transform3D->Position;
            FP bodyRadius = EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);
            FP effectiveRange = action.DamageRange + bodyRadius;

            if (EnemyMovementUtility.TryFindNearestPlayer(f, selfPosition, effectiveRange, out EntityRef hitEntity) == true)
            {
                EnemyMovementUtility.TryGetTargetPosition(f, hitEntity, out FPVector3 hitPosition);

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = filter.Enemy->SkillTargetPosition - selfPosition,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);

                if (StopOnHit == true)
                    return true; // EnemySystem.EnterRecovering restores normal (non-kinematic) movement
            }

            filter.Enemy->StateTimer -= f.DeltaTime;

            FP sqrDistanceToTarget = EnemyMovementUtility.FlatSqrDistance(selfPosition, filter.Enemy->SkillTargetPosition);
            bool arrived = sqrDistanceToTarget <= effectiveRange * effectiveRange;
            bool timedOut = filter.Enemy->StateTimer <= FP._0;

            if (arrived == true || timedOut == true)
            {
                return true; // reached the charge distance without a hit (or timed out) - still consumes cooldown
            }

            FPVector3 moveDelta = filter.Enemy->SkillTargetPosition - selfPosition;

            // bodyRadius + a small epsilon rides on top of WallCheckDistance so a bigger enemy's
            // own collider extent is accounted for too, without needing to retune WallCheckDistance.
            FP stepDistance = FPMath.Max(DashSpeed * f.DeltaTime, WallCheckDistance) + bodyRadius + FP._0_10;
            FPVector3 wallCheckOrigin = selfPosition + FPVector3.Up * WallCheckHeight;

            if (EnemyMovementUtility.IsBlockedByWall(f, wallCheckOrigin, moveDelta, stepDistance, EnemyMovementUtility.GetGroundLayerMask(f)) == true)
            {
                return true; // slammed into static geometry - stop here instead of clipping through it
            }

            EnemyMovementUtility.MoveKinematicTowards(ref filter, data, selfPosition, filter.Enemy->SkillTargetPosition, DashSpeed, f.DeltaTime);
            return false;
        }
    }
}
