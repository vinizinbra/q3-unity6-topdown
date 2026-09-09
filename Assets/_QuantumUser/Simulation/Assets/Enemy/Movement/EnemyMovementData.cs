namespace Quantum
{
    using Photon.Deterministic;

    // Base for every enemy movement style; each subclass is its own Quantum asset owning its move-
    // direction logic, the same "one asset per behavior" shape as AttackData. Shared/reused across
    // enemies via AssetRef<EnemyMovementData>, so instances are never rolled back - ComputeMoveDirection
    // must stay a pure function of (Frame, EntityRef, EntityRef), no mutable fields written at runtime.
    public abstract unsafe partial class EnemyMovementData : AssetObject
    {
        // Opt-in local-avoidance nudge so enemies of the same type don't perfectly stack on top of
        // each other while converging on the same point - off by default. No consumer yet; reserved
        // for whenever a separation pass exists.
        public bool ApplySeparation;

        // Ground-plane (X,Z) direction this enemy wants to move this tick - or default(FPVector2)
        // to mean "don't move". Normally a unit vector (full authored MoveSpeed), but a profile may
        // return a SHORTER vector (magnitude in (0,1)) to ask for proportionally reduced speed along
        // that same heading - MoveInDirection reads the magnitude as a Clamp01 speed scalar (see
        // there), which is how ChaseMovementData eases to a stop across its arrival band. Callers
        // (EnemyMovementUtility.MoveInDirection) apply speed and write velocity; height/altitude is a
        // separate concern (EnemyHeightData), not something this vector encodes.
        public abstract FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target);
    }
}
