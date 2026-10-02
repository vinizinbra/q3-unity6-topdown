namespace Quantum
{
    using Photon.Deterministic;

    // Never moves and can't be moved - a nest, a turret, a totem. See EnemyMovementData.IsKinematic.
    public unsafe class StationaryMovementData : EnemyMovementData
    {
        public override bool IsKinematic => true;

        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target) => default;
    }
}
