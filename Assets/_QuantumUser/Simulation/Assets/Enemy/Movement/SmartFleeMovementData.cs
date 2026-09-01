namespace Quantum
{
    using Photon.Deterministic;

    // FleeMovementData walks straight away from the target with zero awareness of what's actually
    // there - run it at a cliff edge with no jumpable gap/allowed fall and MoveInDirection's own
    // dead-end branch just StopMovements it, letting the target walk right up. This fans the same
    // "straight away" heading out to both sides and commits to the closest-to-ideal one that
    // MoveInDirection would actually be able to carry out (ground ahead, or a gap this enemy can
    // jump, or a drop it's allowed to fall) - i.e. it slides along the cliff instead of freezing at
    // it, and cuts back toward directly-away as soon as a clear heading opens up again. Only the
    // SAFETY verdict is duplicated here (via the same EdgeCheckDistance/GapScanStep constants
    // MoveInDirection itself reads), not the gap/climb/fall handling, so the two can never disagree
    // about what counts as passable.
    public unsafe class SmartFleeMovementData : EnemyMovementData
    {
        // Nearest-to-ideal-heading first, alternating sides, so the first safe candidate found is
        // also the one that runs most directly away from the target. Deliberately stops short of
        // 180 - a heading that only "works" by turning back toward the target isn't fleeing
        // anymore, so the last resort is holding the original heading and letting MoveInDirection's
        // own dead-end handling (climb hop / StopMovement) take over instead.
        private static readonly FP[] DeflectionAngles = { 0, 30, -30, 60, -60, 90, -90, 120, -120, 150, -150 };

        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 delta = new FPVector2(transform->Position.X - targetPosition.X, transform->Position.Z - targetPosition.Z);

            if (delta.SqrMagnitude <= FP._0)
                return default;

            FPVector2 away = delta.Normalized;

            if (f.Unsafe.TryGetPointer<Enemy>(self, out var enemy) == false)
                return away;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            // The cliff/gap probing below only means anything for grounded traversal - a Flying/
            // Airborne enemy never hits MoveInDirection's own dead-end branch, so there's nothing
            // here for it to route around.
            if (data.Stats.Height.InitialState != EnemyHeightState.Grounded)
                return away;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            // Not currently resting on the ground (e.g. mid-knockback) - let physics settle it back
            // down first rather than probing cliff geometry from a mid-air position.
            if (EnemyMovementUtility.IsGrounded(f, self, transform->Position, groundLayerMask, out FP groundY) == false)
                return away;

            FPVector3 groundPosition = new FPVector3(transform->Position.X, groundY, transform->Position.Z);
            FP gapProbeDistance = EnemyMovementUtility.ResolveEntityRadius(f, self) + data.Stats.Height.GapProbeThreshold;

            for (int i = 0; i < DeflectionAngles.Length; i++)
            {
                FPVector2 candidate = FPVector2.Rotate(away, DeflectionAngles[i] * FP.Deg2Rad);

                if (IsHeadingSafe(f, groundPosition, candidate, data, gapProbeDistance, groundLayerMask) == true)
                    return candidate;
            }

            return away;
        }

        // Mirrors MoveInDirection's own currentlyGrounded dead-end check one-for-one (ground ahead,
        // else an allowed gap jump, else an allowed fall) so a heading only ever counts as safe here
        // if MoveInDirection would actually be able to carry it out rather than stopping dead on
        // arrival.
        private static bool IsHeadingSafe(Frame f, FPVector3 groundPosition, FPVector2 direction, EnemyDataAsset data, FP gapProbeDistance, int groundLayerMask)
        {
            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Y);

            if (EnemyMovementUtility.HasGroundAhead(f, groundPosition, flatDirection, gapProbeDistance, EnemyMovementUtility.EdgeCheckDistance, groundLayerMask) == true)
                return true;

            if (data.Stats.Height.CanJumpGaps == true &&
                EnemyMovementUtility.TryFindGapLanding(f, groundPosition, flatDirection, gapProbeDistance, data.Stats.Height.GapDistance, EnemyMovementUtility.GapScanStep, groundLayerMask, out _) == true)
                return true;

            return data.Stats.Height.CanFallFromCliff == true &&
                EnemyMovementUtility.HasGroundWithinFallDistance(f, groundPosition, flatDirection, gapProbeDistance, data.Stats.Height.FallHeight, groundLayerMask) == true;
        }
    }
}
