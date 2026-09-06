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

            // Sourced from the player's actual KCCSettings (not a separately hand-tuned constant)
            // so the wall check stays in sync with whatever collider size/height the character
            // controller is actually using.
            KCCSettings kccSettings = f.FindAsset(filter.KCC->Settings);
            FP wallCheckHeight = kccSettings.Height / 2;
            FP bodyRadius = kccSettings.Radius;

            // HitStatics | HitKinematics (not HitStatics alone) - same combination
            // PlayerMovementProcessor already uses for its own ground/mantle checks. A single sphere
            // sweep over the actual step distance, not a separate overlap-at-destination-then-raycast
            // pair (the previous approach here) - the two could disagree, since the overlap sphere
            // catches a corner/glancing wall that a zero-radius centerline raycast sails clean past,
            // which cancelled the dash in place with zero movement even on a sweep that was otherwise
            // clear. Offset up by wallCheckHeight so a sphere at ground level doesn't overlap the
            // floor collider itself and register as a false "wall" hit - GetGroundLayerMask covers
            // both.
            int wallLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            const QueryOptions WallQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;
            Shape3D bodyShape = Shape3D.CreateSphere(bodyRadius);
            FPVector3 wallCheckOrigin = selfPosition + FPVector3.Up * wallCheckHeight;
            Hit3D? wallHit = f.Physics3D.ShapeCast(wallCheckOrigin, FPQuaternion.Identity, bodyShape, direction * step, wallLayerMask, WallQueryOptions);

            if (wallHit.HasValue == true)
            {
                // CastDistanceNormalized (unlike Point/Normal) doesn't need ComputeDetailedInfo and
                // already accounts for the sphere's own radius against the hit surface at any angle -
                // unlike the old Point - direction * bodyRadius math, which only placed the stop point
                // correctly for a dead-on hit and could leave the sphere still clipping the wall (or
                // stopping short of it) on an angled one.
                FPVector3 stopPosition = selfPosition + direction * step * wallHit.Value.CastDistanceNormalized;
                stopPosition.Y = selfPosition.Y;

                filter.Transform3D->Position = stopPosition;
                Log.Debug($"[Skill] {filter.Entity}'s Dash stopped early at {stopPosition} - blocked by wall");
                return true;
            }

            FPVector3 newPosition = selfPosition + direction * step;
            filter.Transform3D->Position = newPosition;
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
