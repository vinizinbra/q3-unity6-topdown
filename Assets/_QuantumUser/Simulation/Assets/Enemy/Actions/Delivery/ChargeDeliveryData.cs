namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Locked-direction dash ("bull charge"): captures direction the instant the windup ends and
    // commits to a fixed-distance straight line - no re-homing mid-dash, so the player can dodge
    // after the commit point.
    //
    // Moves kinematically (PhysicsBody3D.IsKinematic, via EnemyMovementUtility.MoveKinematicTowards)
    // instead of through Velocity, so collision response doesn't slow/deflect it on contact
    // (EnemySystem.EnterRecovering resets IsKinematic once the action ends). Since that bypasses
    // PhysicsSystem3D's own collision response, Tick runs its own wall check
    // (EnemyMovementUtility.IsBlockedByWall) each step to avoid clipping through geometry - a mid-
    // dash hit also self-stuns the charger (WallStunDuration) rather than just stopping silently.
    //
    // CanBegin (checked BEFORE Preparation/Telegraph even starts, back while still Chasing) runs the
    // same wall check against the full DashDistance so a straight-line path that's walled/ledge-
    // blocked from the current standing spot never gets telegraphed in the first place - the enemy
    // just keeps chasing (closing distance / repositioning) and re-tries next tick instead. This
    // also naturally covers a target on a different-height platform: the flat dash-direction ray
    // catches the platform's own supporting wall the same as any other obstacle. Crucially, a wall
    // hit alone doesn't fail the check - only a wall with no player standing in front of it (same
    // line, at or before the wall) does. A player on the far side of that wall's own base - the
    // ordinary "target standing at a platform's edge, its supporting wall just past them" case - is
    // still a perfectly valid charge, since Tick's own Active-phase loop stops on contact with ANY
    // player long before the dash ever reaches the wall behind them. Once CanBegin has passed and
    // the telegraph actually plays, Begin() is a hard commit - a fair warning shown to the player
    // must always be followed through, even if the target moved into a wall during the windup, so
    // Begin never re-checks the wall and never aborts back to Recovery with zero movement.
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
        // timeout/wall).
        public bool StopOnHit = true;

        // Only relevant while StopOnHit = false - minimum time before this SAME target can be hit
        // again by this charge, instead of every single tick for as long as it stays inside
        // DamageRange. Backed by the optional ChargeHitTracking component, which
        // EnemySystem.SeedChargeHitTracking adds automatically (at spawn time, from this exact
        // StopOnHit=false check) - no Editor authoring needed, unlike EnemyActionSlots/
        // EnemyCombatModifiers which still require hand-adding a component today.
        public FP HitCooldown = FP._0_50;

        // Raycast origin height above GROUND (not above this entity's own pivot - see
        // ResolveWallCheckOrigin below) - keeps the ray inside a wall collider's body instead of
        // skimming the wall/floor seam at ground level, which can graze past it undetected. Kept
        // low and close to the floor on purpose: a taller offset risks clearing over a low wall/
        // railing collider entirely, same failure mode this field exists to avoid at the other end.
        public FP WallCheckHeight = FP.FromString("0.3");

        // Minimum wall-raycast lookahead, regardless of how little ground this tick covers -
        // DashSpeed * DeltaTime alone is too short a ray and can miss a collider by a hair
        // (confirmed by testing - this was the actual cause of charges clipping through walls).
        public FP WallCheckDistance = FP._0_75;

        // Stun applied to the charger itself the instant it slams into a wall mid-dash (Tick's own
        // wall check below) - dazed by its own impact, same StatusEffectUtility.ApplyStun every
        // other stun source uses. 0 opts out (no stun, just stop) with no separate bool needed.
        // NOTE: no-ops on a Boss-tier charger - EnemyTierResistanceConfig's Boss row zeroes out
        // StunDurationMultiplier (deliberately stunlock-immune) - see WallExposedDuration below for
        // the Boss-safe equivalent.
        public FP WallStunDuration = FP._1;

        // Bonus vulnerability window for baiting a wall collision specifically - applied via
        // StatusEffectUtility.ApplyRupture (a plain incoming-damage multiplier, unaffected by the
        // Stun-immunity above since Boss tier's RuptureDurationMultiplier is left at full strength)
        // alongside the WallStunDuration self-stun above. 0 (default) opts out - zero behavior
        // change for the existing Charger or any other charge user. Defaults
        // WallExposedDamageMultiplier to FP._1_50 rather than C#'s own 0 default so that authoring
        // only WallExposedDuration > 0 (an easy mistake) can't accidentally make the wall-hit
        // charger briefly IMMUNE to damage instead of vulnerable - ApplyRupture is a straight
        // multiply with no sanity clamp.
        public FP WallExposedDuration;
        public FP WallExposedDamageMultiplier = FP._1_50;

        public override bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 origin = filter.Transform3D->Position;

            // Not SkillTargetPosition - that's only established once Preparation actually begins
            // (EnemySystem.UpdateChasing sets it the same tick this is called, right after, but
            // CanBegin runs first - see the call site's own ordering) - resolve the live target
            // position directly instead.
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return true; // no target to check against - let TrySelectAction's own gates handle it

            if (TryResolveDirection(data, origin, targetPosition, out FPVector3 direction) == false)
                return true; // already on top of the target - nothing blocking a (degenerate) charge

            FPVector3 wallCheckOrigin = ResolveWallCheckOrigin(f, origin, WallCheckHeight);
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            Hit3D? wallHit = f.Physics3D.Raycast(wallCheckOrigin, direction, DashDistance, groundLayerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);

            if (wallHit.HasValue == false)
                return true; // clear straight line the whole DashDistance - nothing in the way at all

            // A wall exists somewhere along the full DashDistance - but that alone doesn't make the
            // charge un-executable. Tick's own Active-phase loop stops the instant it reaches ANY
            // player (TryFindNearestPlayer), not specifically this target, so a wall standing PAST
            // where a player would be hit is irrelevant - the charge connects and stops long before
            // ever reaching it. This is exactly the "target on an elevated platform" case: the
            // platform's own supporting wall sits just beyond the target, but the charge still lands
            // on the target first. Only a wall with NO player standing in front of it (along this
            // same line, at or before the wall) actually blocks the charge from being worth starting.
            Hit3D? playerHit = f.Physics3D.Raycast(wallCheckOrigin, direction, DashDistance, EnemyMovementUtility.GetPlayerLayerMask(f), QueryOptions.HitAll);

            // A Downed/KO player (see docs/revive.md) standing in the way shouldn't validate the
            // charge on their own - Tick's own hit-connect loop (TryFindNearestPlayer) already
            // skips them too, so treating this raycast as a miss keeps CanBegin/Tick consistent.
            if (playerHit.HasValue == true && PlayerLifeStateUtility.IsIncapacitated(f, playerHit.Value.Entity) == true)
                playerHit = null;

            return playerHit.HasValue == true && playerHit.Value.CastDistanceNormalized <= wallHit.Value.CastDistanceNormalized;
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Uses Enemy.SkillTargetPosition as already established by OnAnticipating, rather
            // than re-resolving the target itself - that would bypass a locked-during-windup
            // telegraph by re-aiming right when the charge starts.
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 resolvedTarget = EnemyMovementUtility.ResolveDestination(data, filter.Enemy->SkillTargetPosition);

            // The telegraph already played and was shown to the player - CanBegin above is what
            // decides whether this action gets picked at all, back while still Chasing. From here
            // on this is a hard commit: no wall re-check, no abort back to Recovery with zero
            // movement. If the resolved delta degenerated to zero (e.g. the target walked exactly
            // on top of this enemy during the windup), fall back to the enemy's current facing
            // direction so there's always somewhere to lunge rather than nowhere.
            if (TryResolveDirection(data, origin, resolvedTarget, out FPVector3 direction) == false)
            {
                FP facingRad = filter.Aim->Angle * FP.Deg2Rad;
                direction = new FPVector3(FPMath.Sin(facingRad), FP._0, FPMath.Cos(facingRad));
            }

            filter.Enemy->SkillTargetPosition = origin + direction * DashDistance;
            filter.Enemy->StateTimer = DashDuration;

            // Kinematic for the whole dash - also why OnInterrupted is never overridden here:
            // DamageUtility.ApplyResolvedImpulse skips a kinematic PhysicsBody3D entirely, so a
            // knockback can never actually reach this delivery while Active regardless of
            // EnemyActionData.InterruptibleDuringActive.
            filter.PhysicsBody3D->IsKinematic = true;

            return false;
        }

        // Shared by CanBegin (checked against the live target position, pre-telegraph) and Begin
        // (checked against the locked SkillTargetPosition, post-telegraph) - false means the delta
        // degenerated to zero (already on top of the target), the one case with no real direction to
        // derive a charge/wall-check from.
        private static bool TryResolveDirection(EnemyDataAsset data, FPVector3 origin, FPVector3 target, out FPVector3 direction)
        {
            bool isFlying = data.Stats.Height.InitialState == EnemyHeightState.Flying;

            FPVector3 delta = isFlying == true
                ? target - origin
                : new FPVector3(target.X - origin.X, FP._0, target.Z - origin.Z);

            if (delta.SqrMagnitude <= FP._0)
            {
                direction = default;
                return false;
            }

            direction = delta.Normalized;
            return true;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Plain f.DeltaTime, not the Void-Pressure-scaled deltaTime further down - cooldown
            // decay is a real-time bookkeeping concern independent of how far the charge itself
            // moves this tick, so it stays simple rather than inheriting the movement slow.
            TickHitCooldowns(f, filter.Entity, f.DeltaTime);

            FPVector3 selfPosition = filter.Transform3D->Position;
            FP bodyRadius = EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);
            FP effectiveRange = action.DamageRange + bodyRadius;

            if (EnemyMovementUtility.TryFindNearestPlayer(f, selfPosition, effectiveRange, out EntityRef hitEntity) == true
                && IsOnHitCooldown(f, filter.Entity, hitEntity) == false)
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

                RecordHit(f, filter.Entity, hitEntity, HitCooldown);
            }

            // Void Pressure (Kai) - scales the whole rest of this Tick (timer, wall-check lookahead,
            // and the actual kinematic move below), not just the timer, so a slowed Charge visibly
            // covers less ground per real second instead of just timing out later - see
            // StatusEffectUtility.GetLocalTimeMultiplier's own comment for why only this Active-phase
            // Tick is ever affected, never the windup/telegraph.
            FP deltaTime = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            filter.Enemy->StateTimer -= deltaTime;

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
            FP stepDistance = FPMath.Max(DashSpeed * deltaTime, WallCheckDistance) + bodyRadius + FP._0_10;
            FPVector3 wallCheckOrigin = ResolveWallCheckOrigin(f, selfPosition, WallCheckHeight);

            if (EnemyMovementUtility.IsBlockedByWall(f, wallCheckOrigin, moveDelta, stepDistance, EnemyMovementUtility.GetGroundLayerMask(f)) == true)
            {
                if (WallStunDuration > FP._0)
                    StatusEffectUtility.ApplyStun(f, filter.Entity, WallStunDuration, filter.Entity);

                if (WallExposedDuration > FP._0)
                    StatusEffectUtility.ApplyRupture(f, filter.Entity, WallExposedDuration, WallExposedDamageMultiplier);

                return true; // slammed into static geometry - stop here (dazed) instead of clipping through it
            }

            EnemyMovementUtility.MoveKinematicTowards(ref filter, data, selfPosition, filter.Enemy->SkillTargetPosition, DashSpeed, deltaTime);
            return false;
        }

        // This entity's own Transform3D.Position is its pivot, not necessarily ground level - e.g. a
        // capsule collider centered at chest height (same caveat EnemyMovementUtility's own leap-
        // landing code already calls out). Raising the wall-check ray off the raw pivot by a fixed
        // WallCheckHeight was floating it well above a wall collider's actual body for any enemy
        // whose pivot isn't already at its feet, letting the charge sail clean over the wall instead
        // of hitting it. Resolving the real ground Y first (TryFindGroundHeight, same helper
        // EnemyMovementUtility.ComputeFlyingHoverVelocity/TryFindClimbLanding/TryFindGapLanding all
        // use) and adding WallCheckHeight to THAT instead keeps the ray pinned close to the floor
        // regardless of this enemy's own pivot height. Falls back to the raw pivot if no ground is
        // found directly below (shouldn't happen mid-level, but never worth a null-deref over).
        private static FPVector3 ResolveWallCheckOrigin(Frame f, FPVector3 position, FP wallCheckHeight)
        {
            FP baseY = EnemyMovementUtility.TryFindGroundHeight(f, position, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY)
                ? groundY
                : position.Y;

            return new FPVector3(position.X, baseY + wallCheckHeight, position.Z);
        }

        // No-op if the enemy prototype doesn't carry the optional ChargeHitTracking component -
        // see StopOnHit/HitCooldown's own comments on why that's a silent gap to author around
        // rather than an error.
        private static void TickHitCooldowns(Frame f, EntityRef entity, FP deltaTime)
        {
            if (f.Unsafe.TryGetPointer<ChargeHitTracking>(entity, out var tracking) == false)
                return;

            for (int i = 0; i < tracking->RecentTargets.Length; i++)
            {
                if (tracking->RecentTargets[i] == EntityRef.None)
                    continue;

                tracking->RecentTargetCooldowns[i] -= deltaTime;

                if (tracking->RecentTargetCooldowns[i] <= FP._0)
                    tracking->RecentTargets[i] = EntityRef.None; // free the slot for a future target
            }
        }

        // False (never on cooldown) if the component isn't present - StopOnHit's own comment
        // covers why that's the deliberate fallback rather than an error.
        private static bool IsOnHitCooldown(Frame f, EntityRef entity, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<ChargeHitTracking>(entity, out var tracking) == false)
                return false;

            for (int i = 0; i < tracking->RecentTargets.Length; i++)
            {
                if (tracking->RecentTargets[i] == target)
                    return tracking->RecentTargetCooldowns[i] > FP._0;
            }

            return false;
        }

        // Claims target's existing slot if it's already tracked (refreshing the cooldown), or the
        // first free slot otherwise. No-op if the component isn't present.
        private static void RecordHit(Frame f, EntityRef entity, EntityRef target, FP cooldown)
        {
            if (f.Unsafe.TryGetPointer<ChargeHitTracking>(entity, out var tracking) == false)
                return;

            for (int i = 0; i < tracking->RecentTargets.Length; i++)
            {
                if (tracking->RecentTargets[i] == target || tracking->RecentTargets[i] == EntityRef.None)
                {
                    tracking->RecentTargets[i] = target;
                    tracking->RecentTargetCooldowns[i] = cooldown;
                    return;
                }
            }

            // Every slot already claimed by a DIFFERENT target - can only happen with more
            // concurrent targets than this project's own co-op player cap (the array's size), an
            // exceedingly rare edge case. Overwrite the first slot rather than silently drop the
            // hit's cooldown tracking entirely.
            tracking->RecentTargets[0] = target;
            tracking->RecentTargetCooldowns[0] = cooldown;
        }
    }
}
