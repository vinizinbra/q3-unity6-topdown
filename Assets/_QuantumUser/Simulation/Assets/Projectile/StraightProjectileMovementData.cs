namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    public unsafe class StraightProjectileMovementData : ProjectileMovementData
    {
        public FP Speed = 20;

        // False (default): velocity points from spawn toward the resolved target/aim, same as before
        // this existed. True: Direction is used instead - X = right, Y = up, Z = forward along the
        // caster's own aim angle (same local-space convention ProjectileSpawner.ResolveSpawnOrigin's
        // own offset uses), rotated onto their current facing at launch. A throw that should always
        // go, say, forward-and-down relative to wherever the player is aiming regardless of pitch.
        public bool OverrideDirection;
        public FPVector3 Direction = FPVector3.Forward;

        // Leads a moving target - any positive value turns leading on (0, the default, aims at the
        // target's exact position, same as before this existed). Only entities with a PhysicsBody3D
        // produce a velocity to lead with - that's every enemy, but not the KCC-driven player, so a
        // shot aimed at a player never leads. Same "positive enables, magnitude doesn't matter"
        // convention as BallisticProjectileMovementData.PredictionTime (see its own comment for why -
        // the real lead comes from ResolveLeadTarget's own refinement, not from this field's value).
        // Without this, a projectile with real flight time (long range / lowish Speed, e.g. Sniper's
        // BasicProjectile) always fires at where a moving target WAS, not where it will be - the shot
        // is launched correctly but the target has already stepped out of the way by the time it
        // arrives.
        public FP PredictionTime = 0;

        protected override ProjectileLaunch SolveLaunch(Frame f, FPVector3 spawnPosition, FPVector3 target, EntityRef targetEntity)
        {
            if (PredictionTime > FP._0 && targetEntity != EntityRef.None && f.Unsafe.TryGetPointer<PhysicsBody3D>(targetEntity, out var targetBody) == true)
            {
                // Clamped to the target's own baseline speed - see ProjectileAimUtility
                // .ResolveLeadVelocity's own comment. Without this, a knocked-back or erratically-
                // steering target's one-tick velocity spike gets extrapolated for the shot's whole
                // flight time, aiming nowhere near where the target will plausibly be.
                FPVector3 leadVelocity = ProjectileAimUtility.ResolveLeadVelocity(f, targetEntity, targetBody->Velocity);
                target = ResolveLeadTarget(spawnPosition, target, leadVelocity, Speed);
            }

            FPVector3 delta = target - spawnPosition;

            if (OverrideDirection == true)
            {
                FP aimAngle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
                delta = FPQuaternion.Euler(0, aimAngle, 0) * Direction;
            }

            if (delta.SqrMagnitude <= FP._0)
                return default;

            return new ProjectileLaunch { Velocity = delta.Normalized * Speed, IsValid = true };
        }

        // Unlike a lob's arc (BallisticProjectileMovementData's own flight time grows with
        // sqrt(distance)), a straight shot's flight time is simply distance/Speed - Speed never
        // changes with how far it travels. Still refined over a few passes rather than solved
        // closed-form (a classic target-leading quadratic), for the same determinism/consistency
        // reasoning as the ballistic version - converges in 1-2 passes in practice since a target's
        // own movement speed is always far slower than a projectile's.
        private static FPVector3 ResolveLeadTarget(FPVector3 spawnPosition, FPVector3 target, FPVector3 targetVelocity, FP speed)
        {
            if (speed <= FP._0)
                return target;

            FPVector3 leadTarget = target;

            for (int i = 0; i < 3; i++)
            {
                FP distance = (leadTarget - spawnPosition).Magnitude;
                FP flightTime = distance / speed;
                leadTarget = target + targetVelocity * flightTime;
            }

            return leadTarget;
        }
    }
}