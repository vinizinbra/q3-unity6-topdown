namespace Quantum
{
    using Photon.Deterministic;

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

        protected override ProjectileLaunch SolveLaunch(FPVector3 spawnPosition, FPVector3 target)
        {
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
    }
}