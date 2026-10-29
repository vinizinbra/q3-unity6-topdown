namespace Quantum
{
    using System;
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

        // 0 (default) keeps the original pure-sphere landing hit. Above zero, a player is only caught
        // if the ACTUAL FLOOR under them is within this many units of the floor at the landing spot -
        // same idiom/field name as GroundAreaDeliveryData/RingSlamDeliveryData.MaxHeightDifference -
        // so a slam on the arena floor doesn't reach a player standing on a ledge above it.
        public FP MaxHeightDifference = FP._0;

        // False (default) keeps the original behavior: wherever SkillTargetPosition ends up, even over
        // a void. True: every windup tick the (possibly still tracking) landing point is pulled back
        // toward the leaper until it sits on real ground no more than MaxLandingHeightDifference away
        // from the leaper's own floor - resolved in OnAnticipating, BEFORE the aim locks, so the
        // telegraph (which reads SkillTargetPosition) always shows the spot it will really land on.
        public bool ResolveValidLanding;

        // Only read with ResolveValidLanding - how far up/down from its own floor the leaper may land.
        public FP MaxLandingHeightDifference = 2;

        // Only read with ResolveValidLanding - how many steps the pull-back search tries.
        private const int LandingSearchSteps = 6;

        public override void OnAnticipating(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target, FP elapsed)
        {
            base.OnAnticipating(f, ref filter, data, action, target, elapsed);

            if (ResolveValidLanding == false || data.Stats.Height.InitialState != EnemyHeightState.Grounded)
                return;

            filter.Enemy->SkillTargetPosition = ResolveLandingSpot(f, filter.Transform3D->Position, filter.Enemy->SkillTargetPosition);
        }

        // Walks from the wanted landing spot back toward the leaper in even steps and returns the first
        // point with ground at a reachable height; falls back to the leaper's own position (a jump in
        // place) when nothing qualifies, so it never commits to a void.
        private FPVector3 ResolveLandingSpot(Frame f, FPVector3 origin, FPVector3 wanted)
        {
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, origin, groundMask, out FP originGroundY) == false)
                return wanted;

            for (int i = 0; i <= LandingSearchSteps; i++)
            {
                FP t = FP._1 - (FP)i / LandingSearchSteps;
                FPVector3 candidate = FPVector3.Lerp(origin, wanted, t);
                candidate.Y = wanted.Y;

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidate, groundMask, out FP groundY) == true &&
                    FPMath.Abs(groundY - originGroundY) <= MaxLandingHeightDifference)
                {
                    return candidate;
                }
            }

            return new FPVector3(origin.X, wanted.Y, origin.Z);
        }

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
            if (data.Stats.Height.InitialState == EnemyHeightState.Grounded &&
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
            // Void Pressure (Yoru) - the timer this jump's whole arc is derived from (see the lerp
            // below) only ticks down at StatusEffectUtility.GetLocalTimeMultiplier's rate while it's
            // in effect, stretching the entire leap through the air in real time rather than just
            // delaying when it resolves - see that method's own comment for why only the Active phase
            // (this Tick, not the windup/telegraph) is ever affected.
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            FP t = JumpDuration > FP._0 ? FPMath.Clamp01(FP._1 - filter.Enemy->StateTimer / JumpDuration) : FP._1;
            FPVector3 flatPosition = FPVector3.Lerp(filter.Enemy->SkillStartPosition, filter.Enemy->SkillTargetPosition, t);
            FP heightOffset = JumpHeight * 4 * t * (FP._1 - t); // parabola, peaks at t=0.5, zero at t=0/1

            filter.Transform3D->Position = new FPVector3(flatPosition.X, flatPosition.Y + heightOffset, flatPosition.Z);

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            // Landed - snap exactly onto the captured spot (avoids any residual lerp drift) and
            // damage every player caught in the blast radius, not just the original target.
            filter.Transform3D->Position = filter.Enemy->SkillTargetPosition;

            // Resolved once per landing, not per candidate - see MaxHeightDifference.
            FP landingGroundY = MaxHeightDifference > FP._0 ? EnemyMovementUtility.ResolveGroundY(f, filter.Enemy->SkillTargetPosition) : default;

            Span<EntityRef> hits = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int hitsCount = EnemyMovementUtility.FindPlayersInRadius(f, filter.Enemy->SkillTargetPosition, action.DamageRange, hits);
            for (int i = 0; i < hitsCount; i++)
            {
                EntityRef hitEntity = hits[i];

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                if (MaxHeightDifference > FP._0 &&
                    FPMath.Abs(EnemyMovementUtility.ResolveGroundY(f, hitTransform->Position) - landingGroundY) > MaxHeightDifference)
                    continue; // standing on a different floor than the one the slam hits

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
