namespace Quantum
{
    using Photon.Deterministic;

    // Leaps toward the target and slams down, damaging every player within DamageRange of the
    // landing spot (an area delivery, unlike ChargeDeliveryData's single-target dash) - airborne
    // the whole way rather than sliding along the ground.
    //
    // Pair with an EnemyActionData authored with EngageRange well beyond DamageRange (same
    // reasoning as Charge - otherwise it triggers standing on top of the target) and DirectionTracking =
    // DoNotUpdateTargetDirection (locks the landing spot at windup start; flip back to
    // UpdateTargetDirectionWhileActive for a homing variant) - see EnemyActionData.DirectionTracking.
    public unsafe class LeapDeliveryData : EnemyDeliveryData
    {
        public FP JumpDuration = FP._0_75;

        // Peak height at the arc's midpoint - purely a visual/feel parameter, doesn't affect
        // where or when it lands.
        public FP JumpHeight = 3;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // SkillTargetPosition is already the landing spot (established by OnAnticipating),
            // so this only records the takeoff point - re-resolving the target here would
            // bypass a locked-during-windup telegraph by re-aiming right at takeoff.
            filter.Enemy->SkillStartPosition = filter.Transform3D->Position;
            filter.Enemy->StateTimer = JumpDuration;

            // Kinematic for the whole jump - also why OnInterrupted is never overridden here, same
            // reasoning as ChargeDeliveryData: a kinematic PhysicsBody3D never receives a real
            // knockback impulse (DamageUtility.ApplyResolvedImpulse skips it), so this delivery's
            // Active phase can't actually be reached via EnemyActionData.InterruptibleDuringActive.
            filter.PhysicsBody3D->IsKinematic = true;

            // SkillTargetPosition.Y is still the target's raw pivot height, not ground level -
            // snapping straight onto the landing surface would sink the enemy's pivot into the
            // ground. Instead, measure how far above ground the enemy's pivot sits at takeoff
            // (where it's already resting correctly) and reapply that same offset at landing.
            // Flying keeps the raw captured Y.
            if (data.Height.InitialState == EnemyHeightState.Grounded &&
                EnemyMovementUtility.TryFindGroundHeight(f, filter.Enemy->SkillStartPosition, EnemyMovementUtility.GetGroundLayerMask(f), out FP takeoffGroundY) == true &&
                EnemyMovementUtility.TryFindGroundHeight(f, filter.Enemy->SkillTargetPosition, EnemyMovementUtility.GetGroundLayerMask(f), out FP landingGroundY) == true)
            {
                FP pivotHeightAboveGround = filter.Enemy->SkillStartPosition.Y - takeoffGroundY;

                FPVector3 landingSpot = filter.Enemy->SkillTargetPosition;
                landingSpot.Y = landingGroundY + pivotHeightAboveGround;
                filter.Enemy->SkillTargetPosition = landingSpot;
            }

            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer -= f.DeltaTime;

            FP t = JumpDuration > FP._0 ? FPMath.Clamp01(FP._1 - filter.Enemy->StateTimer / JumpDuration) : FP._1;
            FPVector3 flatPosition = FPVector3.Lerp(filter.Enemy->SkillStartPosition, filter.Enemy->SkillTargetPosition, t);
            FP heightOffset = JumpHeight * 4 * t * (FP._1 - t); // parabola, peaks at t=0.5, zero at t=0/1

            filter.Transform3D->Position = new FPVector3(flatPosition.X, flatPosition.Y + heightOffset, flatPosition.Z);

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            // Landed - snap exactly onto the captured spot (avoids any residual lerp drift) and
            // damage every player caught in the blast radius, not just the original target.
            filter.Transform3D->Position = filter.Enemy->SkillTargetPosition;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, filter.Enemy->SkillTargetPosition, action.DamageRange);
            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                // Radially outward from the landing spot, not toward wherever each player
                // happens to be facing/moving - a blast pushes everyone away from its center.
                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitTransform->Position,
                    PushDirection = hitTransform->Position - filter.Enemy->SkillTargetPosition,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
            }

            return true;
        }
    }
}
