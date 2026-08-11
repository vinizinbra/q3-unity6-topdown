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
            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == false)
                return;

            orbTransform->Position = anchor;

            if (maxOffset <= FP._0)
            {
                GroundOffsetUtility.Apply(f, orb, orbTransform);
                return;
            }

            FPVector3 landing = EnemyMovementUtility.RandomPositionInRing(f, anchor, minOffset, maxOffset);
            FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y);
            ProjectileLaunch launch = ProjectileSpawner.SolveArcLaunch(anchor, landing, PopLaunchAngle, gravity);

            if (launch.IsValid == false)
            {
                GroundOffsetUtility.Apply(f, orb, orbTransform);
                return;
            }

            f.Add(orb, new PopVelocity { Velocity = launch.Velocity });
        }
    }
}
