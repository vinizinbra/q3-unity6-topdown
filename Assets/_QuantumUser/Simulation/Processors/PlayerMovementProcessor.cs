namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Clone of EnvironmentProcessor for the player: identical gravity/acceleration/friction/
    // stabilization behavior (reuses its static helpers directly), except the kinematic target
    // speed is read per-entity/per-tick from MovementDataAsset + Input.Run instead of a fixed
    // KinematicSpeed field, so walk/run switching actually works.
    //
    // Auto-hop and auto-mantle are also detected here (not in a System) because they need to
    // apply the jump impulse before this tick's move resolves, via SetDynamicVelocity below -
    // reacting a tick late (in a System running after KCCSystem) reads as a visible delay.
    [Preserve]
    public unsafe class PlayerMovementProcessor : KCCProcessor, IBeforeMove, IAfterMoveStep
    {
        private const string NotJumpableLayerName = "GroundNotJumpable";

        // Fallback for a MovementDataAsset authored with MaxFallSpeed <= 0 - matches that field's
        // own default. Deliberately not "unlimited": an unbounded fall is the bug this clamp
        // exists for, so there is no way to opt back into it by leaving the field at 0.
        private static readonly FP DefaultMaxFallSpeed = 30;

        public void BeforeMove(KCCContext context, KCCProcessorInfo processorInfo)
        {
            Frame frame = context.Frame;
            EntityRef entity = context.Entity;

            if (frame.Unsafe.TryGetPointer<PlayerMovement>(entity, out var movement) == false)
                return;
            if (frame.Unsafe.TryGetPointer<PlayerLink>(entity, out var playerLink) == false)
                return;

            MovementDataAsset data = frame.FindAsset(movement->MovementData);
            var input = PlayerInputUtility.Resolve(frame, entity, playerLink);

            // moveDirection stays normalized: HasGroundAhead/TryDetectMantle/TryRerouteAlongShore
            // below all multiply it by a separate probe distance, so they need a true unit vector,
            // not raw analog magnitude. velocityDirection is the one that actually drives speed -
            // kept at its raw (un-normalized) magnitude so light stick input ramps up gently instead
            // of every nonzero tilt snapping straight to full acceleration (KCC.SetInputDirection
            // still clamps it to length 1, and SetKinematicVelocity's own top-speed clamp means this
            // never lets the player move faster than targetSpeed - only how fast they ramp up to it).
            FPVector3 moveDirection = input->Direction != default ? input->Direction.Normalized.XOY : default;
            FPVector3 velocityDirection = input->Direction.XOY;
            FP targetSpeed = input->Run.IsDown ? data.RunSpeed : data.WalkSpeed;

            if (frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                targetSpeed *= stats->MoveSpeedMultiplier;
            }

            // Ice slows, Stun/Root stop movement outright - gravity/friction below still process
            // normally either way, same as knockback not gating movement integration. Root
            // deliberately doesn't touch WeaponSystem/SkillSystem - only movement is pinned, unlike
            // Stun which also silences firing/skills in their own separate checks.
            targetSpeed *= StatusEffectUtility.GetSpeedMultiplier(frame, entity);

            // Store's Energy Drink food offer (see docs/store-blacksmith.md) - composes
            // multiplicatively alongside Ice's own slow, same pattern.
            targetSpeed *= StatusEffectUtility.GetTempMoveSpeedMultiplier(frame, entity);

            // Danger Pay (Rift Mutation) - a CONDITION, not a timed buff, so it can't live in the
            // StatusEffects slots above: it has to appear and disappear the instant health crosses
            // its threshold in either direction, with nothing to expire. Evaluated fresh every
            // movement tick from the same helper the damage half uses, so the two can never
            // disagree about whether the player is currently in danger.
            targetSpeed *= MutationModifierUtility.ResolveLiveMoveSpeedMultiplier(frame, entity);

            // PoiInteractionLockUtility.IsInputLocked - a player with their own Cursed Rift/Store/
            // Blacksmith Choice Window open (see docs/breathing-poi.md/docs/store-blacksmith.md) is
            // locked the same way a Stun/Root already blocks movement, but deliberately NOT via
            // GameplaySystemGroup/Time.timeScale - only this one player's own input is gated,
            // everyone else (and the simulation itself) keeps running normally. A Downed/KO player
            // (see docs/revive.md) is separately, fully pinned via IsIncapacitated - no partial
            // movement for the incapacitated player themselves, only for an Alive reviver (below).
            //
            // ReviveChannel is deliberately carved OUT of the shared zero-speed branch even though
            // IsInputLocked also returns true for it (needed so WeaponSystem/SkillSystem/
            // ContextInteractionSystem still fully lock the reviver's OTHER actions) - a reviver
            // must keep moving at a reduced, not zero, speed (see docs/revive.md).
            bool incapacitated = PlayerLifeStateUtility.IsIncapacitated(frame, entity);
            bool reviving = frame.Has<ReviveChannel>(entity);

            if (StatusEffectUtility.IsStunned(frame, entity) == true || StatusEffectUtility.IsRooted(frame, entity) == true
                || incapacitated == true
                || (PoiInteractionLockUtility.IsInputLocked(frame, entity) == true && reviving == false))
            {
                targetSpeed = FP._0;
            }
            else if (reviving == true)
            {
                ReviveConfig reviveConfig = PlayerLifeStateUtility.GetConfig(frame);
                targetSpeed *= reviveConfig != null ? reviveConfig.ReviveMoveSpeedMultiplier : FP.FromString("0.30");
            }

            context.KCC->Data.Gravity        = data.Gravity;
            context.KCC->Data.MaxGroundAngle = data.MaxGroundAngle;
            context.KCC->Data.MaxWallAngle   = 5;
            context.KCC->Data.MaxHangAngle   = 30;

            // Water edges: resolved before SetInputDirection so a hold/redirect this tick actually
            // changes where the KCC tries to move, not just whether it jumps - see
            // ResolveWaterEdgeMovement for what each outcome (through/redirected/held) does.
            if (context.KCC->Data.IsGrounded == true && moveDirection != default)
            {
                FPVector3 resolvedDirection = ResolveWaterEdgeMovement(context, movement, data, moveDirection);

                // A held edge or a shoreline reroute is a correction the player didn't ask for, not
                // their actual analog input - use it as-is (already default or normalized) rather
                // than the raw magnitude. Anything else (the ordinary "ground ahead" pass-through)
                // leaves velocityDirection at the player's real, magnitude-preserving input.
                if (resolvedDirection != moveDirection)
                    velocityDirection = resolvedDirection;

                moveDirection = resolvedDirection;
            }
            else if (movement->WaterEdgeHesitationTimer > FP._0)
            {
                movement->WaterEdgeHesitationTimer = FP._0;
            }

            context.KCC->SetInputDirection(velocityDirection);

            bool canPredictJump = context.KCC->Data.IsGrounded == true
                && moveDirection != default
                && movement->HasAirJumped == false
                && movement->JumpCooldownTimer <= FP._0;

            if (canPredictJump == true)
            {
                FPVector3 position = context.KCC->Position;

                // Auto-hop: no ground ahead => about to walk off an edge.
                if (HasGroundAhead(context, data, position, moveDirection) == false)
                {
                    DoJump(context.KCC, movement, data);

                    // View-only cue for BlobAnimationView's Jump Flip - see PlayerAutoJumpedDown's
                    // own comment for why this is a dedicated event rather than reusing PlayerJumped.
                    frame.Events.PlayerAutoJumpedDown(entity);
                }
                // Auto-mantle: blocked ahead at foot height but clear above => climbable obstacle.
                else if (TryDetectMantle(context, data, position, moveDirection) == true)
                {
                    DoJump(context.KCC, movement, data);
                }
            }

            EnvironmentProcessor.SetDynamicVelocity(context, ref context.KCC->Data, data.JumpMultiplier, data.DynamicGroundFriction, data.DynamicAirFriction);
            ClampFallSpeed(context, data);
            EnvironmentProcessor.SetKinematicVelocity(context, ref context.KCC->Data, targetSpeed, data.KinematicGroundAcceleration, data.KinematicAirAcceleration, data.KinematicGroundFriction, data.KinematicAirFriction);
        }

        public void AfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo)
        {
            EnvironmentProcessor.ProcessAfterMoveStep(context, processorInfo, overlapInfo);
        }

        // Terminal velocity. SetDynamicVelocity just added another Gravity * dt to DynamicVelocity
        // and applies air friction on XZ only, so nothing in the KCC ever bounds downward speed -
        // a character that never lands accelerates forever. See MovementDataAsset.MaxFallSpeed for
        // why that matters beyond the number getting silly (KCC's CCD loop subdivides by distance
        // and is uncapped, so per-tick physics cost scales with fall speed).
        //
        // Applied HERE, right after the one place gravity accumulates, rather than in a System
        // afterwards: BeforeMove runs once per KCC.Update, before the CCD loop consumes
        // DesiredVelocity, so clamping here bounds this tick's actual movement instead of
        // correcting it a tick late. Downward only - an upward impulse (jump, Discharge's launch,
        // knockback) is untouched, and KinematicVelocity has no Y for a player, so DynamicVelocity
        // is the whole of the fall.
        private static void ClampFallSpeed(KCCContext context, MovementDataAsset data)
        {
            FP maxFallSpeed = data.MaxFallSpeed > FP._0 ? data.MaxFallSpeed : DefaultMaxFallSpeed;

            if (context.KCC->Data.DynamicVelocity.Y >= -maxFallSpeed)
                return;

            context.KCC->Data.DynamicVelocity.Y = -maxFallSpeed;
        }

        private static void DoJump(KCC* kcc, PlayerMovement* movement, MovementDataAsset data)
        {
            kcc->Jump(FPVector3.Up * data.JumpVelocity);
            movement->HasAirJumped = true;
            movement->JumpCooldownTimer = data.JumpCooldownTime;
        }

        private static bool HasGroundAhead(KCCContext context, MovementDataAsset data, FPVector3 position, FPVector3 direction)
        {
            QueryOptions queryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;
            FPVector3 checkOrigin = position + direction * data.EdgeProbeDistance + FPVector3.Up * FP._0_10;

            KCCShapeCastInfo groundCast = KCCShapeCastInfo.Get();
            bool groundAhead = context.KCC->RayCast(context, groundCast, checkOrigin, FPVector3.Down, data.EdgeCheckDistance, queryOptions);
            KCCShapeCastInfo.Return(groundCast);

            return groundAhead;
        }

        // This project has no distinct water collider: every water body is a purely visual plane
        // sitting under the whole map at LevelConfig.FallDeathHeight, mechanically identical to a
        // bottomless pit at any other chunk seam - PlayerFallSystem doesn't distinguish them either.
        // So "is this a water edge" can't be answered by raycasting for water; it's answered by
        // whether a landing exists AT ALL within a crossable distance - a single straight-down probe
        // right at the edge can't tell a narrow, jumpable river (dry land a little further out) from
        // a genuinely uncrossable lake (open water everywhere along its shore), since both look
        // identical directly below the edge. So this scans FORWARD in WaterProbeStep increments out
        // to WaterMaxCrossableGap, testing straight down (to WaterCheckDistance) at each sample -
        // the first one that finds ground stops the scan and counts as a safe landing, same as any
        // ordinary ledge. Mirrors EnemyMovementUtility.TryFindGapLanding, the same problem bots
        // solve for their own void avoidance. Only ever consulted after HasGroundAhead has already
        // returned false.
        private static bool HasSurvivableLandingAhead(KCCContext context, MovementDataAsset data, FPVector3 position, FPVector3 direction)
        {
            QueryOptions queryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

            for (FP distance = data.EdgeProbeDistance + data.WaterProbeStep; distance <= data.WaterMaxCrossableGap; distance += data.WaterProbeStep)
            {
                FPVector3 checkOrigin = position + direction * distance + FPVector3.Up * FP._0_10;

                KCCShapeCastInfo groundCast = KCCShapeCastInfo.Get();
                bool groundAhead = context.KCC->RayCast(context, groundCast, checkOrigin, FPVector3.Down, data.WaterCheckDistance, queryOptions);
                KCCShapeCastInfo.Return(groundCast);

                if (groundAhead == true)
                    return true;
            }

            return false;
        }

        // Water/void edges: HasGroundAhead()==false alone can't tell a legitimate deeper ledge
        // (still fine to auto-hop, and already handled by the existing canPredictJump branch below)
        // from a genuine unrecoverable fall - this only changes anything once
        // HasSurvivableLandingAhead ALSO comes back false. From there:
        //  - a diagonal approach tries sliding onto whichever cardinal axis still has dry ground
        //    (TryRerouteAlongShore) rather than freezing dead against the edge;
        //  - a straight approach with nothing to slide onto is held at the edge (returns default,
        //    which SetInputDirection turns into "stop here") until WaterEdgeInsistTime of
        //    continuous pressing has elapsed, then the original direction is let through - falling
        //    into the same HasGroundAhead==false branch as any other ledge, so it auto-hops in like
        //    the player asked for (needed for an intentional water/gap crossing).
        // Any tick that doesn't reach the "held" case resets the timer, so hesitation only
        // accumulates across a genuinely continuous walk into the same edge.
        private static FPVector3 ResolveWaterEdgeMovement(KCCContext context, PlayerMovement* movement, MovementDataAsset data, FPVector3 moveDirection)
        {
            FPVector3 position = context.KCC->Position;

            if (HasGroundAhead(context, data, position, moveDirection) == true || HasSurvivableLandingAhead(context, data, position, moveDirection) == true)
            {
                movement->WaterEdgeHesitationTimer = FP._0;
                return moveDirection;
            }

            FPVector3 redirected = TryRerouteAlongShore(context, data, position, moveDirection);
            if (redirected != default)
            {
                movement->WaterEdgeHesitationTimer = FP._0;
                return redirected;
            }

            movement->WaterEdgeHesitationTimer += context.Frame.DeltaTime;
            if (movement->WaterEdgeHesitationTimer < data.WaterEdgeInsistTime)
                return default;

            return moveDirection;
        }

        // Diagonal input against a water edge: the X-only and Z-only components of the same
        // direction are tried in turn, and whichever still has dry ground ahead becomes the new
        // movement direction, so the player slides along the shoreline instead of being walled by a
        // check that only understands the one direction the stick is actually pointing. Returns
        // default (no reroute) for an already axis-aligned direction, or if neither axis is dry.
        private static FPVector3 TryRerouteAlongShore(KCCContext context, MovementDataAsset data, FPVector3 position, FPVector3 direction)
        {
            bool hasX = FPMath.Abs(direction.X) > FP._0_01;
            bool hasZ = FPMath.Abs(direction.Z) > FP._0_01;

            if (hasX == false || hasZ == false)
                return default;

            FPVector3 xOnly = new FPVector3(direction.X, FP._0, FP._0).Normalized;
            if (HasGroundAhead(context, data, position, xOnly) == true)
                return xOnly;

            FPVector3 zOnly = new FPVector3(FP._0, FP._0, direction.Z).Normalized;
            if (HasGroundAhead(context, data, position, zOnly) == true)
                return zOnly;

            return default;
        }

        // Ankle-height probe blocked + ledge-height probe clear => climbable obstacle.
        private static bool TryDetectMantle(KCCContext context, MovementDataAsset data, FPVector3 position, FPVector3 direction)
        {
            QueryOptions queryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

            FPVector3 ankleOrigin = position + FPVector3.Up * data.AnkleProbeHeight;

            KCCShapeCastInfo ankleCast = KCCShapeCastInfo.Get();
            bool ankleBlocked = context.KCC->RayCast(context, ankleCast, ankleOrigin, direction, data.MantleProbeDistance, queryOptions);
            KCCShapeCastInfo.Return(ankleCast);

            if (ankleBlocked == false)
                return false;

            // GroundNotJumpable obstacles are solid (in KCCSettings.CollisionLayerMask so they block
            // like a wall) but must never be mantled, regardless of height - checked directly against
            // Physics3D rather than the ledge probe below, since a short GroundNotJumpable obstacle
            // would otherwise still read as a climbable ledge.
            bool ankleBlockedByNotJumpable = context.Frame.Physics3D.Raycast(ankleOrigin, direction, data.MantleProbeDistance, GetNotJumpableLayerMask(context.Frame), queryOptions).HasValue;
            if (ankleBlockedByNotJumpable == true)
                return false;

            FPVector3 ledgeOrigin = position + FPVector3.Up * data.MaxLedgeHeight;

            KCCShapeCastInfo ledgeCast = KCCShapeCastInfo.Get();
            bool ledgeBlocked = context.KCC->RayCast(context, ledgeCast, ledgeOrigin, direction, data.MantleProbeDistance, queryOptions);
            KCCShapeCastInfo.Return(ledgeCast);

            return ledgeBlocked == false;
        }

        // No static caching - f.Layers.GetLayerMask is a cheap lookup into immutable per-match
        // config; a static field would live outside Quantum's Frame/rollback state entirely.
        private static int GetNotJumpableLayerMask(Frame f)
        {
            return f.Layers.GetLayerMask(NotJumpableLayerName);
        }
    }
}
