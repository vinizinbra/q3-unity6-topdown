namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Committed straight charge for a heavy boss - the Frost Patriarch's Glacial Charge. ChargeDeliveryData's sibling with
    // what that one doesn't guarantee:
    //
    //   - ONE hit per player per pass: an IceChargeState hit list, cleared every charge (ChargeDeliveryData's
    //     StopOnHit=false + HitCooldown is a cooldown, not a per-pass rule). Floor-gated (BossHazardUtility.
    //     GatherPlayersOnFloor), never another level sharing XZ. Doesn't stop on hits - it runs through.
    //   - never leaves solid ground: each step needs floor ahead within MaxStepHeight of the current floor, so it stops at
    //     ledges/voids/steps instead of running off them (a plain stop - normal recovery).
    //   - wall slam = a real pause: hitting a wall (arena gates included - they are on the ground layer) stops it for
    //     WallPause seconds, standing still and exposed (Rupture: WallExposedDuration/WallExposedDamageMultiplier, which a
    //     Boss tier takes - unlike a self-stun), shown by its stage-2 visual, before the normal recovery.
    //
    // Direction is locked when the windup ends (no homing). The trail is a GroundTrail passive on the boss with
    // OnlyDuringAction = this action. Interruptible by a stagger break (AllowsForcedPreemption): the state is removed and
    // the boss stops where it is - nothing fires on cancel.
    public unsafe partial class IceChargeDeliveryData : EnemyDeliveryData
    {
        public const byte StageRun = 1;
        public const byte StageWallStun = 2;

        public FP DashSpeed = 14;
        public FP DashDistance = 12;
        public FP MaxStepHeight = FP.FromString("0.6");
        public FP WallCheckHeight = FP.FromString("0.4");
        public FP MaxHeightDifference = FP.FromString("1.2");

        public FP WallPause = FP.FromString("1.2");
        public FP WallExposedDuration = FP.FromString("1.6");
        public FP WallExposedDamageMultiplier = FP._1_50;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 position = filter.Transform3D->Position;
            FPVector3 direction = filter.Enemy->SkillTargetPosition - position;
            direction.Y = FP._0;
            direction = direction.SqrMagnitude > FP._0 ? direction.Normalized : FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;

            f.AddOrGet<IceChargeState>(filter.Entity, out var state);
            state->HitCount = 0;
            state->Direction = direction;
            state->Traveled = FP._0;
            state->WallPause = FP._0;
            state->HitWall = false;

            filter.PhysicsBody3D->IsKinematic = true;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;
            filter.Aim->Angle = FPMath.Atan2(direction.X, direction.Z) * FP.Rad2Deg;
            DeliveryStageUtility.Set(f, filter.Entity, StageRun);
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<IceChargeState>(filter.Entity, out var state) == false)
                return true;

            FP dt = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            if (state->HitWall == true)
            {
                state->WallPause -= dt;
                if (state->WallPause > FP._0)
                    return false;

                Finish(f, filter.Entity);
                return true;
            }

            SweepHits(f, ref filter, state, action);

            FP remaining = DashDistance - state->Traveled;
            if (remaining <= FP._0)
            {
                Finish(f, filter.Entity);
                return true;
            }

            FPVector3 position = filter.Transform3D->Position;
            FP step = FPMath.Min(DashSpeed * dt, remaining);
            FP bodyRadius = EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FP groundY = EnemyMovementUtility.ResolveGroundY(f, position);

            // Wall (or sealed arena gate) ahead: slam, stand there exposed.
            FPVector3 wallOrigin = new FPVector3(position.X, groundY + WallCheckHeight, position.Z);
            if (EnemyMovementUtility.IsBlockedByWall(f, wallOrigin, state->Direction, step + bodyRadius, groundMask) == true)
            {
                state->HitWall = true;
                state->WallPause = WallPause;
                if (WallExposedDuration > FP._0)
                    StatusEffectUtility.ApplyRupture(f, filter.Entity, WallExposedDuration, WallExposedDamageMultiplier);
                DeliveryStageUtility.Set(f, filter.Entity, StageWallStun);
                return false;
            }

            // Ledge / void / step ahead: stop on this floor.
            FPVector3 next = position + state->Direction * step;
            if (EnemyMovementUtility.TryFindGroundHeight(f, next + state->Direction * bodyRadius, groundMask, out FP nextGround, filter.Entity) == false ||
                FPMath.Abs(nextGround - groundY) > MaxStepHeight)
            {
                Finish(f, filter.Entity);
                return true;
            }

            filter.Transform3D->Position = new FPVector3(next.X, position.Y + (nextGround - groundY), next.Z);
            state->Traveled += step;
            return false;
        }

        private void SweepHits(Frame f, ref EnemySystem.Filter filter, IceChargeState* state, EnemyActionData action)
        {
            FPVector3 position = filter.Transform3D->Position;
            FP groundY = EnemyMovementUtility.ResolveGroundY(f, position);
            FP reach = action.DamageRange + EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, new FPVector3(position.X, groundY, position.Z), groundY, reach, MaxHeightDifference, players);

            for (int i = 0; i < count; i++)
            {
                EntityRef player = players[i];

                if (AlreadyHit(state, player) == true || state->HitCount >= state->Hit.Length)
                    continue;

                state->Hit[state->HitCount++] = player;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == false)
                    continue;

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = player,
                    Position = transform->Position,
                    PushDirection = state->Direction,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }
        }

        private static bool AlreadyHit(IceChargeState* state, EntityRef player)
        {
            for (int i = 0; i < state->HitCount; i++)
            {
                if (state->Hit[i] == player)
                    return true;
            }

            return false;
        }

        private static void Finish(Frame f, EntityRef entity)
        {
            f.Remove<IceChargeState>(entity);
            DeliveryStageUtility.Clear(f, entity);
        }

        public override bool AllowsForcedPreemption(Frame f, EntityRef entity) => true;

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            Finish(f, filter.Entity);
        }
    }
}
