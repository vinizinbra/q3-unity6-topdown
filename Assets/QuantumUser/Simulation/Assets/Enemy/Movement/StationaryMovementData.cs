namespace Quantum
{
    using Photon.Deterministic;

    // Never moves under its own power - for turret-like enemies whose only mobility (if any) comes
    // from a Delivery (e.g. a teleport/blink) rather than continuous chase movement.
    public unsafe class StationaryMovementData : EnemyMovementData
    {
        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target) => default;
    }
}
