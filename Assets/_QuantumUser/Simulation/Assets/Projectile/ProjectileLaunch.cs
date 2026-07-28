namespace Quantum
{
    using Photon.Deterministic;

    // What a ProjectileMovementData solves for one shot: where it comes into being and how fast it
    // leaves. IsValid is explicit rather than a zero-velocity sentinel because a drop legitimately
    // starts at rest.
    public struct ProjectileLaunch
    {
        public FPVector3 SpawnPosition;
        public FPVector3 Velocity;
        public bool IsValid;
    }
}