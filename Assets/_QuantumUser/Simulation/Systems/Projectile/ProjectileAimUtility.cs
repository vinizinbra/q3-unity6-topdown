namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared aim resolution for anything firing a projectile at the player's current lock - weapons
    // (WeaponSystem) and skills (ProjectileSkillData) both aim at Aim.Target the same way, so "where
    // does this shot go" lives in one place instead of two copies drifting apart.
    public static unsafe class ProjectileAimUtility
    {
        // Full 3D direction toward the aim point when a target is locked (so aim tilts up/down to
        // match its elevation, not just its flat bearing) - falls back to the flat, horizontal-only
        // facing direction when nothing is targeted.
        public static FPVector3 ResolveAimDirection(Frame f, EntityRef target, FPVector3 origin,
            FPVector3 fallbackFlatDirection, bool aimAtCenter)
        {
            if (TryGetAimPoint(f, target, aimAtCenter, out FPVector3 aimPoint) == true)
            {
                FPVector3 delta = aimPoint - origin;

                if (delta.SqrMagnitude > FP._0)
                    return delta.Normalized;
            }

            return fallbackFlatDirection;
        }

        // The projectile's own movement decides where it connects - a lob wants the target's
        // origin, a flat shot wants its collider center. No projectile data (still being authored,
        // or a fire type with no movement asset to ask) reads as centered.
        public static bool ResolveAimsAtCenter(Frame f, AssetRef<ProjectileDataAsset> projectileDataRef)
        {
            if (projectileDataRef.IsValid == false)
                return true;

            ProjectileDataAsset projectileData = f.FindAsset(projectileDataRef);

            return f.FindAsset(projectileData.Movement).AimsAtTargetCenter;
        }

        // Where a shot is meant to connect: the collider's center for a shot that flies into the
        // body, the target's own origin - its feet - for a lob falling onto it. A target with no
        // collider has no center to find and is aimed at where it stands. False when nothing is
        // targeted.
        public static bool TryGetAimPoint(Frame f, EntityRef target, bool aimAtCenter, out FPVector3 aimPoint)
        {
            aimPoint = default;

            if (target == EntityRef.None || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return false;

            aimPoint = targetTransform->Position;

            if (aimAtCenter == false)
                return true;

            if (TryGetCenterOffset(f, target, out FPVector3 offset) == false)
            {
                Log.Debug($"[Aim] target {target} has no collider - aiming at its origin");
                return true;
            }

            aimPoint += offset;

            return true;
        }

        // A heading with its vertical component removed, keeping the same speed - the shared half of
        // the pierce fix used by both fire types (see DirectHitData.FlattenTrajectoryOnPierce and
        // WeaponSystem.FireHitscanPellet). False when there is nothing to do (already level) or
        // nothing left to fly along once the vertical component is dropped (a straight up/down shot),
        // in which case the caller keeps whatever heading it already had.
        public static bool TryFlattenHeading(FPVector3 heading, out FPVector3 flattened)
        {
            flattened = heading;

            if (heading.Y == FP._0)
                return false;

            FPVector3 flat = new FPVector3(heading.X, FP._0, heading.Z);

            if (flat.SqrMagnitude <= FP._0)
                return false;

            flattened = flat.Normalized * heading.Magnitude;

            return true;
        }

        // Pulled out of TryGetAimPoint so a caller already holding a separately-locked target
        // position (e.g. an enemy delivery's Enemy.SkillTargetPosition, frozen mid-windup by
        // AimLock - see ProjectileDeliveryData/FanProjectileDeliveryData) can add just the
        // collider's centroid onto it without discarding that locked X/Z for a live re-read.
        public static bool TryGetCenterOffset(Frame f, EntityRef target, out FPVector3 offset)
        {
            offset = default;

            if (target == EntityRef.None || f.Unsafe.TryGetPointer<PhysicsCollider3D>(target, out var collider) == false)
                return false;

            offset = collider->Shape.Centroid;

            // Centroid is an offset in the target's local space, so it has to ride the target's
            // rotation to stay on the body of one that isn't axis-aligned.
            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
                offset = targetTransform->Rotation * offset;

            return true;
        }
    }
}
