namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Fixed-direction dash in the player's current movement input direction (falls back to Aim
    // facing if no movement input is held) - full distance captured at Begin, no re-homing
    // mid-dash. Moves by writing Transform3D.Position directly each tick (KCC.SetActive(false) for
    // the duration, the player-side equivalent of PhysicsBody3D.IsKinematic on ChargeDeliveryData/
    // EnemyMovementUtility.MoveKinematicTowards) so PlayerMovementProcessor's normal input-driven
    // movement doesn't fight the dash. Deliberately NOT KCC.Teleport: KCC.Update
    // (Simulation/Core/KCC.cs) re-derives Data.BasePosition/DesiredPosition/TargetPosition fresh
    // from Transform3D.Position every tick regardless of IsActive, so a plain write is already
    // fully safe against desync - Teleport's extra Data-sync is unneeded here, and its side effect
    // of also calling Transform3D.Teleport() flags every single tick as a hard teleport, which
    // suppresses view-side interpolation and reads as stutter when done every tick of a multi-tick
    // move (confirmed the actual cause of a reported stutter bug). Runs its own wall check each
    // step for the same reason ChargeDeliveryData does - direct writes bypass the KCC's own
    // collision resolution.
    //
    // Swaps the dasher's collider onto IgnoreProjectile for the duration so a shot passes through
    // instead of being consumed on contact for zero damage, and so the dasher's own collider stops
    // physically blocking/being blocked by Enemy bodies (QuantumDefaultConfigs' physics layer
    // matrix excludes IgnoreProjectile from the Enemy layer - a code-level layer swap alone can't
    // change that, the engine-level collision response is purely matrix-driven). End restores it
    // to Player.
    public unsafe partial class DashSkillData : SkillData
    {
        public FP DashSpeed = 20;
        public FP DashDistance = 6;

        // Safety timeout in case the dash never arrives.
        public FP DashDuration = FP._0_50;

        public FP WallCheckDistance = FP._0_75;

        // "Arrived" threshold - kept tight since a dash has no Range/connect-distance concept.
        private static readonly FP ArrivalDistance = FP._0_10;

        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            FPVector3 direction = ResolveDashDirection(filter, input);

            slot->TargetPosition = slot->StartPosition + direction * DashDistance;
            slot->StateTimer = DashDuration;

            filter.KCC->SetActive(false);
            SetLayer(f, filter.Entity, EnemyMovementUtility.GetIgnoreProjectileLayerIndex(f));

            Log.Debug($"[Skill] {filter.Entity} began Dash from {slot->StartPosition} toward {slot->TargetPosition}");
            return false; // a dash is never instant
        }

        public override bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            FPVector3 selfPosition = filter.Transform3D->Position;

            slot->StateTimer -= f.DeltaTime;

            FP sqrDistanceToTarget = EnemyMovementUtility.FlatSqrDistance(selfPosition, slot->TargetPosition);
            bool arrived = sqrDistanceToTarget <= ArrivalDistance * ArrivalDistance;
            bool timedOut = slot->StateTimer <= FP._0;

            if (arrived == true || timedOut == true)
            {
                return true;
            }

            FPVector3 moveDelta = slot->TargetPosition - selfPosition;
            FPVector3 direction = moveDelta.Normalized;
            FP step = FPMath.Min(DashSpeed * f.DeltaTime, moveDelta.Magnitude);
            FPVector3 candidatePosition = selfPosition + direction * step;

            // Sourced from the player's actual KCCSettings (not a separately hand-tuned constant)
            // so the wall check stays in sync with whatever collider size/height the character
            // controller is actually using.
            KCCSettings kccSettings = f.FindAsset(filter.KCC->Settings);
            FP wallCheckHeight = kccSettings.Height / 2;
            FP bodyRadius = kccSettings.Radius;

            // HitStatics | HitKinematics (not HitStatics alone) - same combination
            // PlayerMovementProcessor already uses for its own ground/mantle checks. A single
            // fixed-height raycast (the previous approach here) turned out to actually let the dash
            // pass through level-chunk walls; a sphere overlap at the candidate position is robust
            // to wall geometry/height instead of depending on guessing the right ray height.
            // Offset up by wallCheckHeight (same height used below for the stop-point raycast) -
            // GetGroundLayerMask covers the floor as well as walls, so a sphere centered at
            // candidatePosition's own (ground-level) height would overlap the floor collider itself
            // on every tick and register as a false "wall" hit.
            int wallLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            const QueryOptions WallQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;
            Shape3D bodyShape = Shape3D.CreateSphere(bodyRadius);
            FPVector3 wallCheckPosition = candidatePosition + FPVector3.Up * wallCheckHeight;
            var wallHits = f.Physics3D.OverlapShape(wallCheckPosition, FPQuaternion.Identity, bodyShape, wallLayerMask, WallQueryOptions);

            if (wallHits.Count > 0)
            {
                // Best-effort tighter stop point via a raycast (same widened query) - falls back to
                // just not moving this tick (stay at selfPosition, already confirmed clear) if the
                // ray doesn't happen to connect, rather than risk clipping into the wall anyway.
                FPVector3 wallCheckOrigin = selfPosition + FPVector3.Up * wallCheckHeight;
                FP stepDistance = FPMath.Max(step, WallCheckDistance);
                Hit3D? wallHit = f.Physics3D.Raycast(wallCheckOrigin, direction, stepDistance, wallLayerMask, WallQueryOptions);

                FPVector3 stopPosition = selfPosition;

                if (wallHit.HasValue == true)
                {
                    stopPosition = wallHit.Value.Point - direction * bodyRadius;
                    stopPosition.Y = selfPosition.Y;
                }

                filter.Transform3D->Position = stopPosition;
                Log.Debug($"[Skill] {filter.Entity}'s Dash stopped early at {stopPosition} - blocked by wall");
                return true;
            }

            filter.Transform3D->Position = candidatePosition;
            return false;
        }

        public override void End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            filter.KCC->SetActive(true);
            SetLayer(f, filter.Entity, EnemyMovementUtility.GetPlayerLayerIndex(f));

            Log.Debug($"[Skill] {filter.Entity}'s Dash ended at {filter.Transform3D->Position}");
        }

        private static void SetLayer(Frame f, EntityRef entity, int layerIndex)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Layer = (byte)layerIndex;
            }
        }

        private static FPVector3 ResolveDashDirection(SkillSystem.Filter filter, Input* input)
        {
            if (input->Direction != default)
            {
                return input->Direction.Normalized.XOY;
            }

            return FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;
        }
    }
}
