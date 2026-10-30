namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Channelled cone breath - the Frost Patriarch's Polar Breath. The windup's aim lock (EnemyActionData.AimLock) fixes the
    // starting direction; while channelling, the breath turns toward the live target at no more than TurnRateDegrees (deg/s)
    // - never a snap or instant retarget - writing Aim.Angle, which the view (BreathConeFxDirector) also reads, so the
    // effect and the hit area always match. Author the action with DirectionTracking = DoNotUpdateTargetDirection.
    //
    // Every TickInterval: players inside the cone (action.DamageRange, ConeAngleDegrees around Aim.Angle), on the
    // breather's own floor (MaxHeightDifference) and in line of sight (no wall/ledge between mouth height and them) take
    // action.Damage + action.Effects (e.g. ChillEffectData: slow first, Freeze if they stay in). Frozen players are skipped
    // (SkipFrozenTargets) so a Freeze is never followed by unavoidable breath damage. Interruptible by a stagger break
    // (AllowsForcedPreemption) - all state lives on Enemy, nothing to clean up, and nothing fires on cancel.
    public unsafe partial class BreathConeDeliveryData : EnemyDeliveryData
    {
        public FP Duration = FP.FromString("2.5");
        public FP TickInterval = FP._0_25;
        public FP ConeAngleDegrees = 60;
        public FP TurnRateDegrees = 35;
        public FP MaxHeightDifference = FP.FromString("1.2");
        // Line-of-sight ray height above the breather's floor.
        public FP MouthHeight = FP._1;
        public bool SkipFrozenTargets = true;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 toLocked = filter.Enemy->SkillTargetPosition - filter.Transform3D->Position;
            if (toLocked.X != FP._0 || toLocked.Z != FP._0)
                filter.Aim->Angle = FPMath.Atan2(toLocked.X, toLocked.Z) * FP.Rad2Deg;

            filter.Enemy->StateTimer = Duration;
            filter.Enemy->PendingImpactIndex = 0;
            return Tick(f, ref filter, data, action, target);
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FP dt = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            if (filter.Enemy->PendingImpactIndex > 0)
                filter.Enemy->StateTimer -= dt;

            TurnTowardTarget(f, ref filter, target, dt);

            FP elapsed = Duration - filter.Enemy->StateTimer;
            while (filter.Enemy->PendingImpactIndex < 255 && filter.Enemy->PendingImpactIndex * TickInterval <= elapsed &&
                   filter.Enemy->PendingImpactIndex * TickInterval < Duration)
            {
                FireTick(f, ref filter, action);
                filter.Enemy->PendingImpactIndex++;
            }

            return filter.Enemy->StateTimer <= FP._0;
        }

        private void TurnTowardTarget(Frame f, ref EnemySystem.Filter filter, EntityRef target, FP dt)
        {
            if (TurnRateDegrees <= FP._0 || EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return;

            FPVector3 delta = targetPosition - filter.Transform3D->Position;
            if (delta.X == FP._0 && delta.Z == FP._0)
                return;

            FP desired = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
            FP difference = desired - filter.Aim->Angle;

            while (difference > 180)
                difference -= 360;
            while (difference < -180)
                difference += 360;

            FP maxStep = TurnRateDegrees * dt;
            filter.Aim->Angle += FPMath.Clamp(difference, -maxStep, maxStep);
        }

        private void FireTick(Frame f, ref EnemySystem.Filter filter, EnemyActionData action)
        {
            FPVector3 origin = filter.Transform3D->Position;
            FP groundY = EnemyMovementUtility.ResolveGroundY(f, origin);
            FPVector3 center = new FPVector3(origin.X, groundY, origin.Z);
            FPVector3 direction = FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;
            FP arcCos = FPMath.Cos(ConeAngleDegrees * FP._0_50 * FP.Deg2Rad);
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, center, groundY, action.DamageRange, MaxHeightDifference, players);

            for (int i = 0; i < count; i++)
            {
                EntityRef player = players[i];

                if (SkipFrozenTargets == true && StatusEffectUtility.IsFrozen(f, player) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == false)
                    continue;

                FPVector3 toPlayer = transform->Position - center;
                toPlayer.Y = FP._0;
                FP distance = toPlayer.Magnitude;

                if (distance > FP._0 && FPVector3.Dot(direction, toPlayer / distance) < arcCos)
                    continue;

                // Walls and ledges block the breath - no hitting through geometry.
                FPVector3 mouth = center + FPVector3.Up * MouthHeight;
                if (distance > FP._0 && EnemyMovementUtility.IsBlockedByWall(f, mouth, toPlayer, distance, groundMask) == true)
                    continue;

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = player,
                    Position = transform->Position,
                    PushDirection = direction,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral, // the cold comes only from the authored ChillEffectData
                    // Distinct per tick - identical EntityDamaged events in one frame would collapse into one.
                    HitIndex = filter.Enemy->PendingImpactIndex,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }
        }

        public override bool AllowsForcedPreemption(Frame f, EntityRef entity) => true;
    }
}
