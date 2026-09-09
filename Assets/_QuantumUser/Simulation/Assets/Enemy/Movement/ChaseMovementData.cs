namespace Quantum
{
    using Photon.Deterministic;

    // Walks straight at the target's live position every tick - no path-finding, no lead/predict.
    // Layers a graceful arrival band on top of that original chase: beyond SlowDistance it runs at
    // full MoveSpeed exactly as before, across (StopDistance, SlowDistance) it eases speed down by
    // returning a progressively shorter direction vector (EnemyMovementUtility.MoveInDirection reads
    // that magnitude as a 0..1 speed scalar - see its own comment), and at/inside StopDistance it
    // returns default to stop and hold its stand-off instead of slamming into and juddering against
    // the target. SlowDistance <= StopDistance collapses the band back to the original
    // (pre-modular) EnemySystem chase behavior - full speed right up to StopDistance (0 = no limit).
    public unsafe class ChaseMovementData : EnemyMovementData
    {
        // Hold at least this far from the target; the enemy eases to a stop here rather than
        // overlapping it. Independent of attack selection (EnemySystem.UpdateChasing switches to
        // Preparation at an action's own EngageRange) - keep StopDistance <= the smallest action
        // EngageRange, or the enemy parks just outside its own attack reach and never commits.
        public FP StopDistance = 1;

        // Start easing down from full speed once within this distance of the target. Must be
        // > StopDistance to give the ramp any width; <= StopDistance disables the ramp entirely
        // (full speed straight up to StopDistance, then a hard hold - no overlap, just not eased).
        public FP SlowDistance = 3;

        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 delta = new FPVector2(targetPosition.X - transform->Position.X, targetPosition.Z - transform->Position.Z);

            if (delta.SqrMagnitude <= FP._0)
                return default;

            // Beyond the band, or no band authored: full-speed unit vector, identical to the
            // original chase. Squared compare so the common far case skips the Magnitude sqrt below.
            if (SlowDistance <= StopDistance || delta.SqrMagnitude > SlowDistance * SlowDistance)
                return delta.Normalized;

            FP distance = delta.Magnitude;

            // At or inside StopDistance: hold. default() means "don't move" (MoveInDirection stops
            // cleanly), so the enemy settles at the stand-off instead of creeping on at a tiny speed.
            if (distance <= StopDistance)
                return default;

            // Inside the band: ramp speed linearly from 0 (at StopDistance) to 1 (at SlowDistance)
            // by returning a shortened direction whose magnitude IS that scalar.
            FP speedScale = (distance - StopDistance) / (SlowDistance - StopDistance);
            return delta.Normalized * speedScale;
        }
    }
}
