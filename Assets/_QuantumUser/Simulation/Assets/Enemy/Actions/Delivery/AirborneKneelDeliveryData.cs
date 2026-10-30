namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Stagger break / exposure for a boss that may be in the air (Dono do Céu). KneelDeliveryData's job, plus:
    //
    //   1 Hit    HitTime   - recoil pose, still where it was hit
    //   2 Fall             - only if it is above AirborneThreshold: a controlled (kinematic, eased) drop onto a
    //                        VALID floor - right below, else the boss arena's spawn point, never into geometry
    //   3 Kneel  Duration  - Rupture (extra damage taken) starts HERE, after touchdown, never mid-fall
    //   4 Rise   RiseTime  - getting back up, still exposed-free and harmless
    //
    // On Begin it also cancels the boss's running mechanics (tornadoes dissipate with no pulse, future crowd throws
    // and marks dropped - BossHazardUtility.CancelOwnedHazards). Never preemptable; interrupted anyway = snapped onto
    // its landing point.
    public unsafe partial class AirborneKneelDeliveryData : EnemyDeliveryData
    {
        public const byte StageHit = 1;
        public const byte StageFall = 2;
        public const byte StageKneel = 3;
        public const byte StageRise = 4;

        public FP HitTime = FP.FromString("0.3");
        public FP FallSpeed = 9;
        public FP MinFallTime = FP.FromString("0.35");
        public FP Duration = 3;
        public FP DamageMultiplier = FP.FromString("1.5");
        public FP RiseTime = FP.FromString("0.6");

        // Height above its floor (beyond its resting pivot) that counts as "airborne" - below it, no fall stage.
        public FP AirborneThreshold = FP.FromString("0.6");

        public bool CancelOwnedHazards = true;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (CancelOwnedHazards == true)
                BossHazardUtility.CancelOwnedHazards(f, filter.Entity);

            filter.PhysicsBody3D->IsKinematic = true;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;

            FPVector3 position = filter.Transform3D->Position;
            FP pivot = EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);

            f.AddOrGet<AirborneKneelState>(filter.Entity, out var state);
            state->FallFrom = position;
            state->FallTo = ResolveLanding(f, filter.Entity, position, pivot);

            bool airborne = position.Y - state->FallTo.Y > AirborneThreshold;
            EnterStage(f, filter.Entity, state, StageHit, airborne ? HitTime : HitTime * FP._0_75);
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<AirborneKneelState>(filter.Entity, out var state) == false)
                return true;

            state->StageTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            switch (state->Stage)
            {
                case StageHit:
                    if (state->StageTimer > FP._0)
                        break;

                    FP drop = state->FallFrom.Y - state->FallTo.Y;
                    if (drop > AirborneThreshold || FPVector3.Distance(state->FallFrom, state->FallTo) > FP._0_50)
                        EnterStage(f, filter.Entity, state, StageFall, FPMath.Max(MinFallTime, FPVector3.Distance(state->FallFrom, state->FallTo) / FallSpeed));
                    else
                        EnterKneel(f, ref filter, state);
                    break;

                case StageFall:
                    FP t = FP._1 - FPMath.Clamp01(state->StageTimer / FPMath.Max(MinFallTime, FPVector3.Distance(state->FallFrom, state->FallTo) / FallSpeed));
                    filter.Transform3D->Position = FPVector3.Lerp(state->FallFrom, state->FallTo, t * t); // accelerates down

                    if (state->StageTimer <= FP._0)
                        EnterKneel(f, ref filter, state);
                    break;

                case StageKneel:
                    if (state->StageTimer <= FP._0)
                        EnterStage(f, filter.Entity, state, StageRise, RiseTime);
                    break;

                case StageRise:
                    if (state->StageTimer <= FP._0)
                    {
                        Finish(f, ref filter);
                        return true;
                    }
                    break;
            }

            return false;
        }

        // Touchdown: snapped exactly onto the landing point, THEN the exposure window starts.
        private void EnterKneel(Frame f, ref EnemySystem.Filter filter, AirborneKneelState* state)
        {
            filter.Transform3D->Position = state->FallTo;
            StatusEffectUtility.ApplyRupture(f, filter.Entity, Duration, DamageMultiplier);
            EnterStage(f, filter.Entity, state, StageKneel, Duration);
        }

        // Floor right below; else the boss arena's own spawn point (same fallback EnemyFallSystem uses for a boss);
        // else stay put. Always resting pivot height above the found floor.
        public static FPVector3 ResolveLanding(Frame f, EntityRef entity, FPVector3 position, FP pivot)
        {
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, position, groundMask, out FP groundY, entity) == true && groundY <= position.Y)
                return new FPVector3(position.X, groundY + pivot, position.Z);

            if (LevelGenerationSystem.TryFindBossArenaChunk(f, out EntityRef chunk) == true)
            {
                var points = new List<FPVector3>();
                LevelGenerationSystem.ResolveBossSpawnPositions(f, chunk, points);

                if (points.Count > 0 && EnemyMovementUtility.TryFindGroundHeight(f, points[0], groundMask, out FP spawnGround, entity) == true)
                    return new FPVector3(points[0].X, spawnGround + pivot, points[0].Z);
            }

            return position;
        }

        private static void EnterStage(Frame f, EntityRef entity, AirborneKneelState* state, byte stage, FP duration)
        {
            state->Stage = stage;
            state->StageTimer = duration;
            DeliveryStageUtility.Set(f, entity, stage);
        }

        private static void Finish(Frame f, ref EnemySystem.Filter filter)
        {
            f.Remove<AirborneKneelState>(filter.Entity);
            DeliveryStageUtility.Clear(f, filter.Entity);
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            if (f.Unsafe.TryGetPointer<AirborneKneelState>(filter.Entity, out var state) == true && state->Stage <= StageFall)
                filter.Transform3D->Position = state->FallTo;

            Finish(f, ref filter);
        }
    }
}
