namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Keeps Anchored enemies kinematic and still. Runs at the end of the gameplay group: the enemy
    // state machine (recovery, knockback, stagger, root) and pushes like Juggernaut's explosion flip
    // IsKinematic back off or write a velocity during the tick, and this re-pins it before the next
    // physics step - one place, instead of special-casing every one of those writes.
    [Preserve]
    public unsafe class AnchoredSystem : SystemMainThreadFilter<AnchoredSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            // Once, on the first tick it can find ground: pivot = ground + the collider's own
            // half-height (same clearance GroundSettleSystem uses). Done here rather than at spawn so
            // every spawn path is covered, including ones that set the position after seeding.
            if (filter.Anchored->Placed == false &&
                EnemyMovementUtility.TryFindGroundHeight(f, filter.Transform3D->Position, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY, filter.Entity) == true)
            {
                FPVector3 position = filter.Transform3D->Position;
                filter.Transform3D->Position = new FPVector3(position.X, groundY + GroundOffsetUtility.ResolveGroundClearance(f, filter.Entity), position.Z);
                filter.Anchored->Placed = true;
            }

            filter.PhysicsBody3D->IsKinematic = true;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;
            filter.PhysicsBody3D->AngularVelocity = FPVector3.Zero;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public PhysicsBody3D* PhysicsBody3D;
            public Anchored* Anchored;
        }
    }
}
