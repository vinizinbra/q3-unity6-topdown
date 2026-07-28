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
        public void BeforeMove(KCCContext context, KCCProcessorInfo processorInfo)
        {
            Frame frame = context.Frame;
            EntityRef entity = context.Entity;

            if (frame.Unsafe.TryGetPointer<PlayerMovement>(entity, out var movement) == false)
                return;
            if (frame.Unsafe.TryGetPointer<PlayerLink>(entity, out var playerLink) == false)
                return;

            MovementDataAsset data = frame.FindAsset(movement->MovementData);
            var input = frame.GetPlayerInput(playerLink->Player);

            FPVector3 moveDirection = input->Direction != default ? input->Direction.Normalized.XOY : default;
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

            if (StatusEffectUtility.IsStunned(frame, entity) == true || StatusEffectUtility.IsRooted(frame, entity) == true)
            {
                targetSpeed = FP._0;
            }

            context.KCC->Data.Gravity        = data.Gravity;
            context.KCC->Data.MaxGroundAngle = data.MaxGroundAngle;
            context.KCC->Data.MaxWallAngle   = 5;
            context.KCC->Data.MaxHangAngle   = 30;

            context.KCC->SetInputDirection(moveDirection);

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
                }
                // Auto-mantle: blocked ahead at foot height but clear above => climbable obstacle.
                else if (TryDetectMantle(context, data, position, moveDirection) == true)
                {
                    DoJump(context.KCC, movement, data);
                }
            }

            EnvironmentProcessor.SetDynamicVelocity(context, ref context.KCC->Data, data.JumpMultiplier, data.DynamicGroundFriction, data.DynamicAirFriction);
            EnvironmentProcessor.SetKinematicVelocity(context, ref context.KCC->Data, targetSpeed, data.KinematicGroundAcceleration, data.KinematicAirAcceleration, data.KinematicGroundFriction, data.KinematicAirFriction);
        }

        public void AfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo)
        {
            EnvironmentProcessor.ProcessAfterMoveStep(context, processorInfo, overlapInfo);
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

            FPVector3 ledgeOrigin = position + FPVector3.Up * data.MaxLedgeHeight;

            KCCShapeCastInfo ledgeCast = KCCShapeCastInfo.Get();
            bool ledgeBlocked = context.KCC->RayCast(context, ledgeCast, ledgeOrigin, direction, data.MantleProbeDistance, queryOptions);
            KCCShapeCastInfo.Return(ledgeCast);

            return ledgeBlocked == false;
        }
    }
}
