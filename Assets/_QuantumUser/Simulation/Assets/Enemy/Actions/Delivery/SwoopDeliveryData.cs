namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // A grounded boss's flying dive - Dono do Céu's "Rasante do Capitão". All of the flight lives inside this one
    // Active delivery (kinematic, like LeapDeliveryData), so the boss stays an ordinary Grounded enemy everywhere
    // else and no shared flight reader (gravity, hover spring, telegraph ground offset) has to change:
    //
    //   windup (AnticipationStep)  - takeoff crouch on the ground
    //   1 Rise   RiseTime          - straight up to HoverAltitude (real height, the shadow stays on the floor)
    //   2 Hover  HoverTime         - drifts to a stand-off point beside the target, wall/void checked
    //   3 Aim    AimTime           - direction and impact point LOCKED: a STRAIGHT line from the hovering boss down to
    //                                the target's spot on the target's own floor (a platform included - never the floor
    //                                under the boss), wall-checked, then a skim along that floor (trimmed at walls, voids
    //                                and floors more than MaxLandingHeightDifference away from it)
    //   4 Dive                     - along that path at DiveSpeed; each player under the skimming boss is hit at most once
    //   end                        - lands at the lane end (valid floor), then the action's DownTime is the grounded
    //                                punish window (EndStep = landing pose)
    //
    // Stagger: stages 1-3 allow forced preemption (AllowsForcedPreemption) - the boss is left in the air and the
    // forced AirborneKneelDeliveryData brings it down; the short committed dive finishes first (deferred).
    public unsafe partial class SwoopDeliveryData : EnemyDeliveryData
    {
        public const byte StageRise = 1;
        public const byte StageHover = 2;
        public const byte StageAim = 3;
        public const byte StageDive = 4;

        public FP RiseTime = FP._0_50;
        public FP HoverAltitude = FP.FromString("3.5");
        public FP HoverTime = FP.FromString("1.4");
        public FP HoverSpeed = 6;
        public FP HoverStandOff = 5;
        public FP AimTime = FP.FromString("0.75");

        public FP DiveSpeed = 15;
        // Total flat length of approach + skim; the skim past the impact point is never shorter than MinOvershoot.
        public FP DiveLength = 12;
        public FP MinOvershoot = 3;

        // Players are only hit while the boss is at most this far above its floor (the skim part of the dive).
        public FP HitHeight = FP.FromString("2.2");
        // Player floor vs the floor under the boss.
        public FP MaxHeightDifference = FP.FromString("1.2");
        // The skim stops before any floor further than this from the impact floor (ledges, pits, other levels).
        public FP MaxLandingHeightDifference = FP.FromString("1.5");
        public FP LaneSampleStep = FP._0_50;

        public bool BlockWhileMajorMechanicActive = true;

        // Flat lane decal on the floor (GroundLineWarning) for the aim + dive. Off when the dive is telegraphed by its 3D
        // flight path instead (SwoopTrajectoryTelegraphDirector draws every SwoopState in Aim/Dive) - never both.
        public bool ShowGroundLaneWarning = true;

        public override bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (BlockWhileMajorMechanicActive == true && BossHazardUtility.IsMajorMechanicActive(f, filter.Entity) == true)
                return false;

            return EnemyMovementUtility.TryFindGroundHeight(f, filter.Transform3D->Position, EnemyMovementUtility.GetGroundLayerMask(f), out _, filter.Entity);
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 position = filter.Transform3D->Position;
            FP groundY = EnemyMovementUtility.TryFindGroundHeight(f, position, EnemyMovementUtility.GetGroundLayerMask(f), out FP found, filter.Entity)
                ? found
                : position.Y - EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);

            filter.PhysicsBody3D->IsKinematic = true;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;

            f.AddOrGet<SwoopState>(filter.Entity, out var state);
            state->GroundY = groundY;
            state->PivotOffset = position.Y - groundY;
            state->HitCount = 0;
            state->DiveStart = position;
            EnterStage(f, filter.Entity, state, StageRise, RiseTime);
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<SwoopState>(filter.Entity, out var state) == false)
                return true;

            FP dt = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            state->StageTimer -= dt;

            switch (state->Stage)
            {
                case StageRise:
                    TickRise(f, ref filter, state);
                    break;
                case StageHover:
                    TickHover(f, ref filter, state, target, action, dt);
                    break;
                case StageAim:
                    if (state->StageTimer <= FP._0)
                    {
                        state->DiveStart = filter.Transform3D->Position;
                        FP length = FPVector3.Distance(Flat(state->DiveStart), Flat(state->DiveEnd));
                        EnterStage(f, filter.Entity, state, StageDive, FPMath.Max(length / DiveSpeed, FP._0_10));
                    }
                    break;
                case StageDive:
                    if (TickDive(f, ref filter, state, action) == true)
                    {
                        Finish(f, ref filter);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private void TickRise(Frame f, ref EnemySystem.Filter filter, SwoopState* state)
        {
            FP t = FP._1 - FPMath.Clamp01(state->StageTimer / RiseTime);
            FPVector3 position = filter.Transform3D->Position;
            position.Y = state->GroundY + state->PivotOffset + HoverAltitude * t * (2 - t); // ease-out climb
            filter.Transform3D->Position = position;

            if (state->StageTimer <= FP._0)
                EnterStage(f, filter.Entity, state, StageHover, HoverTime);
        }

        private void TickHover(Frame f, ref EnemySystem.Filter filter, SwoopState* state, EntityRef target, EnemyActionData action, FP dt)
        {
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 position = filter.Transform3D->Position;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                EnemyMovementUtility.FaceTarget(filter.Aim, position, targetPosition);

                // Stand-off point on the boss's side of the target - circles in rather than parking on top of it.
                FPVector3 away = Flat(position - targetPosition);
                FPVector3 desired = away.SqrMagnitude > FP._0
                    ? targetPosition + away.Normalized * HoverStandOff
                    : position;

                FPVector3 delta = Flat(desired - position);
                FP distance = delta.Magnitude;

                if (distance > FP._0_10)
                {
                    FPVector3 direction = delta / distance;
                    FP step = FPMath.Min(HoverSpeed * dt, distance);
                    FPVector3 next = position + direction * step;

                    // Never drift through a wall or out over a void/other level.
                    if (EnemyMovementUtility.IsBlockedByWall(f, new FPVector3(position.X, state->GroundY + FP._1, position.Z), direction, step + FP._0_50, groundMask) == false &&
                        EnemyMovementUtility.TryFindGroundHeight(f, next, groundMask, out FP nextGround, filter.Entity) == true &&
                        FPMath.Abs(nextGround - state->GroundY) <= MaxLandingHeightDifference)
                    {
                        position = next;
                    }
                }
            }

            position.Y = state->GroundY + state->PivotOffset + HoverAltitude;
            filter.Transform3D->Position = position;

            if (state->StageTimer <= FP._0)
                BeginAim(f, ref filter, state, target, action);
        }

        private void BeginAim(Frame f, ref EnemySystem.Filter filter, SwoopState* state, EntityRef target, EnemyActionData action)
        {
            FPVector3 position = filter.Transform3D->Position;
            FPVector3 direction = FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                FPVector3 toTarget = Flat(targetPosition - position);
                if (toTarget.SqrMagnitude > FP._0)
                {
                    direction = toTarget.Normalized;
                    EnemyMovementUtility.FaceTarget(filter.Aim, position, targetPosition);
                }
            }

            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 start = Flat(position);

            // Impact: the target's spot on the target's own floor, at the boss's resting body height. The straight
            // approach to it must be clear - if a wall/ledge/gate is in the way it stops just short of it, on whatever
            // floor is below there.
            FP impactDistance = FP._0;
            FPVector3 impact = start;
            FP impactGround = state->GroundY;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 aimTarget) == true)
            {
                impactDistance = FPVector3.Dot(Flat(aimTarget) - start, direction);
                impactDistance = FPMath.Clamp(impactDistance, FP._0, DiveLength);
                impact = start + direction * impactDistance;

                if (EnemyMovementUtility.TryFindGroundHeight(f, impact, groundMask, out FP targetGround, filter.Entity) == true)
                    impactGround = targetGround;

                FPVector3 impactBody = new FPVector3(impact.X, impactGround + state->PivotOffset, impact.Z);
                FPVector3 approach = impactBody - position;
                FP approachLength = approach.Magnitude;

                if (approachLength > FP._0)
                {
                    var blocked = f.Physics3D.Raycast(position, approach / approachLength, approachLength, groundMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);

                    if (blocked.HasValue == true)
                    {
                        FP clear = FPMath.Max(FP._0, FPVector3.Dot(Flat(blocked.Value.Point) - start, direction) - FP._1);
                        impactDistance = FPMath.Min(impactDistance, clear);
                        impact = start + direction * impactDistance;
                        impactGround = EnemyMovementUtility.TryFindGroundHeight(f, impact, groundMask, out FP shortGround, filter.Entity) == true && shortGround <= position.Y
                            ? shortGround
                            : state->GroundY;
                    }
                }
            }

            // Skim along the impact floor, stopping before walls, voids and other levels - the landing point is always
            // the last valid floor sample.
            FPVector3 end = impact;
            FP endGround = impactGround;
            FP skim = FPMath.Max(MinOvershoot, DiveLength - impactDistance);

            for (FP traveled = LaneSampleStep; traveled <= skim; traveled += LaneSampleStep)
            {
                FPVector3 sample = impact + direction * traveled;
                FPVector3 wallProbe = new FPVector3(end.X, endGround + FP._0_50, end.Z);

                if (EnemyMovementUtility.IsBlockedByWall(f, wallProbe, direction, LaneSampleStep, groundMask) == true)
                    break;

                if (EnemyMovementUtility.TryFindGroundHeight(f, sample, groundMask, out FP sampleGround, filter.Entity) == false ||
                    FPMath.Abs(sampleGround - impactGround) > MaxLandingHeightDifference)
                    break;

                end = new FPVector3(sample.X, FP._0, sample.Z);
                endGround = sampleGround;
            }

            state->ImpactDistance = impactDistance;
            state->DiveEnd = new FPVector3(end.X, endGround + state->PivotOffset, end.Z);
            state->HoverPoint = direction; // locked lane direction (flat, unit)

            FP laneLength = FPVector3.Distance(start, Flat(end));
            FP width = action.DamageRange * 2;
            if (ShowGroundLaneWarning == true)
                f.Events.GroundLineWarning(new FPVector3(impact.X, impactGround, impact.Z), new FPVector3(end.X, endGround, end.Z), width, AimTime + laneLength / DiveSpeed, filter.Entity);

            EnterStage(f, filter.Entity, state, StageAim, AimTime);
        }

        // Returns true once the boss has landed at the lane end.
        private bool TickDive(Frame f, ref EnemySystem.Filter filter, SwoopState* state, EnemyActionData action)
        {
            FP t = state->StageDuration > FP._0 ? FP._1 - FPMath.Clamp01(state->StageTimer / state->StageDuration) : FP._1;
            FPVector3 flat = FPVector3.Lerp(Flat(state->DiveStart), Flat(state->DiveEnd), t);
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            FP groundY = EnemyMovementUtility.TryFindGroundHeight(f, flat, groundMask, out FP found, filter.Entity)
                ? found
                : state->DiveEnd.Y - state->PivotOffset;

            // Straight line down to the impact point, then skimming the floor (EvaluateDiveY).
            FP y = EvaluateDiveY(f, state, flat, groundY, groundMask, filter.Entity);
            FP height = y - (groundY + state->PivotOffset);

            filter.Transform3D->Position = new FPVector3(flat.X, y, flat.Z);

            if (height <= HitHeight)
                SweepHits(f, ref filter, state, action, flat, groundY);

            if (state->StageTimer > FP._0)
                return false;

            filter.Transform3D->Position = state->DiveEnd;
            return true;
        }

        // Body height at a point of the locked path: before the impact point, the straight line from DiveStart to the
        // impact floor (+ resting pivot); after it, the floor under the boss (+ pivot). Shared with the 3D telegraph.
        public static FP EvaluateDiveY(Frame f, SwoopState* state, FPVector3 flat, FP groundUnder, int groundMask, EntityRef self)
        {
            FPVector3 start = Flat(state->DiveStart);
            FP traveled = FPVector3.Distance(start, flat);

            if (state->ImpactDistance <= FP._0 || traveled >= state->ImpactDistance)
                return groundUnder + state->PivotOffset;

            FPVector3 impact = start + state->HoverPoint * state->ImpactDistance;
            FP impactGround = EnemyMovementUtility.TryFindGroundHeight(f, impact, groundMask, out FP found, self) ? found : groundUnder;
            return FPMath.Lerp(state->DiveStart.Y, impactGround + state->PivotOffset, traveled / state->ImpactDistance);
        }

        private void SweepHits(Frame f, ref EnemySystem.Filter filter, SwoopState* state, EnemyActionData action, FPVector3 flat, FP groundY)
        {
            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, new FPVector3(flat.X, groundY, flat.Z), groundY, action.DamageRange, MaxHeightDifference, players);

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
                    PushDirection = state->HoverPoint, // along the lane
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }
        }

        private static bool AlreadyHit(SwoopState* state, EntityRef player)
        {
            for (int i = 0; i < state->HitCount; i++)
            {
                if (state->Hit[i] == player)
                    return true;
            }

            return false;
        }

        private static void EnterStage(Frame f, EntityRef entity, SwoopState* state, byte stage, FP duration)
        {
            state->Stage = stage;
            state->StageTimer = duration;
            state->StageDuration = duration;
            DeliveryStageUtility.Set(f, entity, stage);
        }

        private static void Finish(Frame f, ref EnemySystem.Filter filter)
        {
            f.Remove<SwoopState>(filter.Entity);
            DeliveryStageUtility.Clear(f, filter.Entity);
        }

        private static FPVector3 Flat(FPVector3 v) => new FPVector3(v.X, FP._0, v.Z);

        // Stagger break while still climbing/hovering/aiming: cut short, the boss stays where it is in the air and
        // the forced AirborneKneelDeliveryData takes it down. The committed dive itself is never cut.
        public override bool AllowsForcedPreemption(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<SwoopState>(entity, out var state) == true && state->Stage != StageDive;
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            Finish(f, ref filter);
        }
    }
}
