namespace Quantum
{
    using Photon.Deterministic;

    // Shared "place a just-created pickup orb at an enemy's death position and pop it out to a
    // scattered landing spot" helper for CoinUtility/RiftShardUtility/ScrapUtility (ExpOrb
    // deliberately always spawns exactly on the death point instead - see
    // ExperienceUtility.TrySpawnDrop - so it never needed this). Previously each of those three
    // just teleported straight to a scattered XZ at the enemy's own death Y and asked
    // GroundOffsetUtility to ease the Y in afterward - which could render the orb below the ground
    // mesh for a moment (or leave it there for good, if the raycast at the new XZ missed
    // entirely) whenever the scattered spot's real terrain height differed from the death point's,
    // e.g. scattering onto a ledge/ramp the flat XZ-only offset doesn't account for. Popping it out
    // on a real ballistic arc from the death position instead - re-resolving ground height every
    // tick via PopMotionSystem, the same "never trust a stale Y" idea GroundOffsetUtility itself
    // already applies once at spawn - means the orb only ever moves through space directly above
    // wherever it currently is, so it can never be rendered underground mid-flight.
    public static unsafe class OrbSpawnUtility
    {
        // Same default ProjectileDeliveryData/BallisticProjectileMovementData/FanProjectileDeliveryData
        // already use for their own lobs - a decisive, already-proven-legible arc, not a new number.
        private static readonly FP PopLaunchAngle = 45;

        public static void SpawnWithPop(Frame f, EntityRef orb, FPVector3 anchor, FP minOffset, FP maxOffset)
        {
            SpawnWithPop(f, orb, anchor, minOffset, maxOffset, FP._0, FP._0);
        }

        // Same as above, plus an optional random "burst" velocity layered on top of the solved arc -
        // each drop gets a random horizontal direction (speed 0..randomHorizontalSpeed) and a random
        // upward kick (0..randomVerticalSpeed), so a pile of drops off one break scatters organically
        // instead of every one tracing the identical 45-degree arc to its ring point. Both 0 (what the
        // 5-arg overload passes) reproduces the original arc-only behavior exactly, so every existing
        // caller (Coin/RiftShard/Scrap) is unchanged. PopMotionSystem re-resolves real ground under
        // the orb every tick, so any launch velocity lands safely - a bigger random kick just travels
        // further before settling, never underground.
        public static void SpawnWithPop(Frame f, EntityRef orb, FPVector3 anchor, FP minOffset, FP maxOffset,
            FP randomHorizontalSpeed, FP randomVerticalSpeed)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == false)
                return;

            orbTransform->Position = anchor;

            FPVector3 velocity = ResolveRandomPopVelocity(f, randomHorizontalSpeed, randomVerticalSpeed);

            // The floor this drop belongs to - see PopVelocity.OriginGroundY. Falls back to the anchor's
            // own Y when no ground is found beneath it (an enemy killed out over a pit), which simply
            // means the orb is never treated as climbing and behaves exactly as it did before.
            FP originGroundY = EnemyMovementUtility.TryFindGroundHeight(f, anchor, EnemyMovementUtility.GetGroundLayerMask(f), out FP anchorGroundY)
                ? anchorGroundY
                : anchor.Y;

            if (maxOffset > FP._0)
            {
                FPVector3 landing = EnemyMovementUtility.RandomPositionInRing(f, anchor, minOffset, maxOffset);
                FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y);
                ProjectileLaunch launch = ProjectileSpawner.SolveArcLaunch(anchor, landing, PopLaunchAngle, gravity);

                if (launch.IsValid == true)
                    velocity += launch.Velocity;
            }

            if (velocity.SqrMagnitude <= FP._0)
            {
                // Neither a ring scatter nor a random kick was requested (or the arc solve failed with
                // no random component to fall back on) - just ground-snap in place, exactly as the
                // arc-only path did before.
                GroundOffsetUtility.Apply(f, orb);
                return;
            }

            f.Add(orb, new PopVelocity { Velocity = velocity, OriginGroundY = originGroundY });
        }

        // Deterministic random burst - a random compass direction at a random speed in
        // [0, horizontalSpeed], plus a random upward speed in [0, verticalSpeed]. Same f.RNG->Next
        // idiom EnemyMovementUtility.RandomPositionInRing uses, so it stays lockstep across clients.
        private static FPVector3 ResolveRandomPopVelocity(Frame f, FP horizontalSpeed, FP verticalSpeed)
        {
            FPVector3 velocity = FPVector3.Zero;

            if (horizontalSpeed > FP._0)
            {
                FP angle = f.RNG->Next(0, 360);
                FP speed = f.RNG->Next(FP._0, horizontalSpeed);
                velocity += FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward * speed;
            }

            if (verticalSpeed > FP._0)
            {
                velocity.Y += f.RNG->Next(FP._0, verticalSpeed);
            }

            return velocity;
        }
    }
}
