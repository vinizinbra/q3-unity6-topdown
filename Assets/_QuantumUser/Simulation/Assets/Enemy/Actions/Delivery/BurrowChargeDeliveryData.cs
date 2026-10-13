namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // "Drill charge" (Rift Drillbreaker): dives into the sand, travels underground to an entry point
    // LeadInDistance from the target, marks a line on the ground through the target, then erupts at
    // the line's start and charges along it - and when the charge ends the drill gets stuck for a
    // moment (planted + Rupture-exposed), the punish window.
    //
    // Stages (BurrowChargeState.Stage, one Active phase start to finish):
    //   Dive    - sinks in place (DiveDuration). Invulnerable + Burrowed from here...
    //   Travel  - moves underground at BurrowMoveSpeed toward the entry point (MaxTravelDuration cap).
    //   Mark    - holds still underground; the line is locked and GroundLineWarning shows it (MarkDuration).
    //   Emerge  - rises at the line start (EmergeDuration). ...to here: tags removed at the end.
    //   Charge  - kinematic dash along the line at ChargeSpeed, hits each player once, stops at the
    //             line end or early on a wall.
    //   Stuck   - planted for StuckDuration with Rupture (ExposedDamageMultiplier) - not a Stun, so
    //             Elite stun resistance/immunity can't shorten the window.
    //
    // Reuses BurrowDeliveryData's tags (Burrowed/Invulnerable - so the view hides the rig and
    // targeting skips it) and ChargeDeliveryData's wall check. action.Damage/DamageRange/Effects are
    // the charge hit; the line's width is DamageRange * 2. Pair with a large EngageRange and a long
    // CooldownTime, same as any burrow action.
    public unsafe class BurrowChargeDeliveryData : EnemyDeliveryData
    {
        private const string BurrowedLayerName = "DeadEnemy";

        public const byte StageDive = 0;
        public const byte StageTravel = 1;
        public const byte StageMark = 2;
        public const byte StageEmerge = 3;
        public const byte StageCharge = 4;
        public const byte StageStuck = 5;

        public FP DiveDuration = FP._0_50;
        public FP DiveDepth = 2;

        // Underground move speed toward the entry point (units/second).
        public FP BurrowMoveSpeed = 9;

        // Travel gives up and marks from wherever it is after this long (<= 0 = no cap).
        public FP MaxTravelDuration = FP._2;

        // Entry point = target + (target -> enemy direction, rotated by up to +-EntryAngleJitter
        // degrees) * LeadInDistance - the charge always comes from roughly the side it dove from,
        // with some variety, and always has this much runway before reaching the target.
        public FP LeadInDistance = 5;
        public FP EntryAngleJitter = 50;

        // How long the line is shown before it erupts - the dodge window.
        public FP MarkDuration = FP._1;

        public FP EmergeDuration = FP._0_25;

        public FP ChargeSpeed = 16;

        // Total charge length from the line start (passes LeadInDistance through the target and
        // keeps going). Trimmed short of a void at Mark time so it never charges off a ledge.
        public FP ChargeDistance = 11;

        // The drill is stuck this long after the charge ends (line end or wall), taking
        // ExposedDamageMultiplier x damage. action.DownTime still follows as normal Recovery.
        public FP StuckDuration = FP._1_50;
        public FP ExposedDamageMultiplier = FP._1_50;

        // Same meaning as ChargeDeliveryData's fields of the same name.
        public FP WallCheckHeight = FP.FromString("0.3");
        public FP WallCheckDistance = FP._0_75;

        private const int EntryAttempts = 6;
        private const int LineSampleCount = 12;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 start = filter.Transform3D->Position;
            filter.Enemy->SkillStartPosition = start;
            filter.Enemy->SkillTargetPosition = ResolveEntryPoint(f, filter.Entity, start, filter.Enemy->SkillTargetPosition);
            filter.PhysicsBody3D->IsKinematic = true;

            f.Add<Invulnerable>(filter.Entity);
            f.Add<Burrowed>(filter.Entity);
            f.Add<BurrowChargeState>(filter.Entity);

            if (f.Unsafe.TryGetPointer<Burrowed>(filter.Entity, out var burrowed) == true &&
                f.Unsafe.TryGetPointer<PhysicsCollider3D>(filter.Entity, out var collider) == true)
            {
                // Same as BurrowDeliveryData: off the Enemy layer while underground so it doesn't
                // shove anyone along its path. Restored at the end of Emerge.
                burrowed->PreviousLayer = (byte)collider->Layer;
                collider->Layer = (byte)f.Layers.GetLayerIndex(BurrowedLayerName);
            }

            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<BurrowChargeState>(filter.Entity, out var state) == false)
                return true;

            FP dt = f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            state->StageElapsed += dt;

            switch (state->Stage)
            {
                case StageDive:
                {
                    FPVector3 start = filter.Enemy->SkillStartPosition;
                    FP t = DiveDuration > FP._0 ? FPMath.Clamp01(state->StageElapsed / DiveDuration) : FP._1;
                    filter.Transform3D->Position = new FPVector3(start.X, start.Y - DiveDepth * t, start.Z);

                    if (t >= FP._1)
                        EnterStage(state, StageTravel);

                    return false;
                }

                case StageTravel:
                {
                    FPVector3 current = filter.Transform3D->Position;
                    FPVector3 entry = filter.Enemy->SkillTargetPosition;
                    FPVector3 toEntry = new FPVector3(entry.X - current.X, FP._0, entry.Z - current.Z);
                    FP distance = toEntry.Magnitude;
                    FP step = BurrowMoveSpeed * dt;

                    if (distance > step)
                    {
                        filter.Transform3D->Position = current + toEntry / distance * step;
                        filter.Aim->Angle = FPMath.Atan2(toEntry.X, toEntry.Z) * FP.Rad2Deg;

                        if (MaxTravelDuration <= FP._0 || state->StageElapsed < MaxTravelDuration)
                            return false;
                    }
                    else
                    {
                        filter.Transform3D->Position = current + toEntry;
                    }

                    BeginMark(f, ref filter, data, action, state, target);
                    return false;
                }

                case StageMark:
                {
                    if (state->StageElapsed < MarkDuration)
                        return false;

                    EnterStage(state, StageEmerge);
                    return false;
                }

                case StageEmerge:
                {
                    FPVector3 lineStart = state->LineStart;
                    FP rise = EmergeDuration > FP._0 ? FPMath.Clamp01(state->StageElapsed / EmergeDuration) : FP._1;
                    filter.Transform3D->Position = new FPVector3(lineStart.X, lineStart.Y - DiveDepth * (FP._1 - rise), lineStart.Z);

                    if (rise < FP._1)
                        return false;

                    filter.Transform3D->Position = lineStart;
                    Surface(f, filter.Entity);
                    EnterStage(state, StageCharge);
                    return false;
                }

                case StageCharge:
                    if (TickCharge(f, ref filter, data, action, state, dt) == true)
                        EnterStuck(f, filter.Entity, state);

                    return false;

                default: // StageStuck
                    if (state->StageElapsed < StuckDuration)
                        return false;

                    f.Remove<BurrowChargeState>(filter.Entity);
                    return true;
            }
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            Cleanup(f, ref filter);
        }

        // Never leaves an enemy hidden/invulnerable/sunk if the action is cut off mid-way.
        private void Cleanup(Frame f, ref EnemySystem.Filter filter)
        {
            if (f.Has<Burrowed>(filter.Entity) == true)
            {
                if (f.Unsafe.TryGetPointer<BurrowChargeState>(filter.Entity, out var state) == true && state->Stage >= StageMark)
                    filter.Transform3D->Position = state->LineStart;
                else
                    filter.Transform3D->Position = filter.Enemy->SkillStartPosition;

                Surface(f, filter.Entity);
            }

            if (f.Has<BurrowChargeState>(filter.Entity) == true)
                f.Remove<BurrowChargeState>(filter.Entity);
        }

        private static void EnterStage(BurrowChargeState* state, byte stage)
        {
            state->Stage = stage;
            state->StageElapsed = FP._0;
        }

        private static void Surface(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Burrowed>(entity, out var burrowed) == true &&
                f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Layer = burrowed->PreviousLayer;
            }

            if (f.Has<Invulnerable>(entity) == true)
                f.Remove<Invulnerable>(entity);

            if (f.Has<Burrowed>(entity) == true)
                f.Remove<Burrowed>(entity);
        }

        // Picks a grounded entry point around the target, re-rolling the side jitter a few times;
        // falls back to diving in place (the line then runs from here through the target).
        private FPVector3 ResolveEntryPoint(Frame f, EntityRef self, FPVector3 selfPosition, FPVector3 targetPosition)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 away = new FPVector3(selfPosition.X - targetPosition.X, FP._0, selfPosition.Z - targetPosition.Z);
            FPVector3 baseDirection = away.SqrMagnitude > FP._0 ? away.Normalized : FPVector3.Forward;

            for (int attempt = 0; attempt < EntryAttempts; attempt++)
            {
                FP angle = EntryAngleJitter > FP._0 ? f.RNG->Next(-EntryAngleJitter, EntryAngleJitter) : FP._0;
                FPVector3 candidate = targetPosition + FPQuaternion.Euler(0, angle, 0) * baseDirection * LeadInDistance;

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidate, groundLayerMask, out FP groundY) == true &&
                    BurrowDeliveryData.IsPathClear(f, selfPosition, candidate, groundLayerMask) == true)
                {
                    return new FPVector3(candidate.X, groundY, candidate.Z);
                }
            }

            Log.Debug($"[Enemy] {self} drill charge found no clear entry point near {targetPosition} - emerging in place");
            return selfPosition;
        }

        // Locks the line: from the current (underground) spot through the target's LIVE position,
        // ChargeDistance long, trimmed back to the last grounded sample before any void.
        private void BeginMark(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, BurrowChargeState* state, EntityRef target)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 here = filter.Transform3D->Position;
            FPVector3 surfaceProbe = new FPVector3(here.X, here.Y + DiveDepth, here.Z);

            FP groundY = EnemyMovementUtility.TryFindGroundHeight(f, surfaceProbe, groundLayerMask, out FP foundY) == true
                ? foundY
                : filter.Enemy->SkillStartPosition.Y;

            FPVector3 lineStart = new FPVector3(here.X, groundY, here.Z);

            FPVector3 aimPoint = EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true
                ? targetPosition
                : filter.Enemy->SkillTargetPosition;

            FPVector3 toAim = new FPVector3(aimPoint.X - lineStart.X, FP._0, aimPoint.Z - lineStart.Z);
            FPVector3 direction;

            if (toAim.SqrMagnitude > FP._0)
            {
                direction = toAim.Normalized;
            }
            else
            {
                FP facingRad = filter.Aim->Angle * FP.Deg2Rad;
                direction = new FPVector3(FPMath.Sin(facingRad), FP._0, FPMath.Cos(facingRad));
            }

            FP length = FP._0;

            for (int i = 1; i <= LineSampleCount; i++)
            {
                FP distance = ChargeDistance * i / LineSampleCount;

                if (EnemyMovementUtility.TryFindGroundHeight(f, lineStart + direction * distance, groundLayerMask, out _) == false)
                    break;

                length = distance;
            }

            state->LineStart = lineStart;
            state->LineEnd = lineStart + direction * length;
            filter.Aim->Angle = FPMath.Atan2(direction.X, direction.Z) * FP.Rad2Deg;
            EnterStage(state, StageMark);

            FP chargeTime = ChargeSpeed > FP._0 ? length / ChargeSpeed : FP._0;
            f.Events.GroundLineWarning(state->LineStart, state->LineEnd, action.DamageRange * 2, MarkDuration + EmergeDuration + chargeTime, filter.Entity);
        }

        // True once the charge is over (line end reached or a wall hit).
        private bool TickCharge(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, BurrowChargeState* state, FP dt)
        {
            FPVector3 selfPosition = filter.Transform3D->Position;
            FP bodyRadius = EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);
            FP hitRadius = action.DamageRange + bodyRadius;

            Span<EntityRef> hits = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int hitsCount = EnemyMovementUtility.FindPlayersInRadius(f, selfPosition, hitRadius, hits);

            for (int i = 0; i < hitsCount; i++)
            {
                EntityRef hitEntity = hits[i];

                if (PlayerLifeStateUtility.IsIncapacitated(f, hitEntity) == true || TryRecordHit(state, hitEntity) == false)
                    continue;

                EnemyMovementUtility.TryGetTargetPosition(f, hitEntity, out FPVector3 hitPosition);

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = state->LineEnd - state->LineStart,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
            }

            FPVector3 toEnd = new FPVector3(state->LineEnd.X - selfPosition.X, FP._0, state->LineEnd.Z - selfPosition.Z);

            if (toEnd.SqrMagnitude <= FP._0)
                return true;

            FP stepDistance = FPMath.Max(ChargeSpeed * dt, WallCheckDistance) + bodyRadius + FP._0_10;
            FPVector3 wallCheckOrigin = ChargeDeliveryData.ResolveWallCheckOrigin(f, selfPosition, WallCheckHeight);

            if (EnemyMovementUtility.IsBlockedByWall(f, wallCheckOrigin, toEnd, stepDistance, EnemyMovementUtility.GetGroundLayerMask(f)) == true)
                return true; // drilled into a wall - stuck right here

            EnemyMovementUtility.MoveKinematicTowards(ref filter, data, selfPosition, state->LineEnd, ChargeSpeed, dt);
            return false;
        }

        private void EnterStuck(Frame f, EntityRef entity, BurrowChargeState* state)
        {
            EnterStage(state, StageStuck);

            if (StuckDuration > FP._0 && ExposedDamageMultiplier > FP._0)
                StatusEffectUtility.ApplyRupture(f, entity, StuckDuration, ExposedDamageMultiplier);
        }

        // False if this player was already hit by this charge.
        private static bool TryRecordHit(BurrowChargeState* state, EntityRef target)
        {
            for (int i = 0; i < state->HitTargets.Length; i++)
            {
                if (state->HitTargets[i] == target)
                    return false;

                if (state->HitTargets[i] == EntityRef.None)
                {
                    state->HitTargets[i] = target;
                    return true;
                }
            }

            return false; // more players than slots - never happens with the 4-player cap
        }
    }
}
