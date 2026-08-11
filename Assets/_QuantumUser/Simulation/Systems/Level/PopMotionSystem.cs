namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Integrates a spawned orb's PopVelocity arc (from OrbSpawnUtility.SpawnWithPop) each tick,
    // gravity-accelerated the same way GroundSettleSystem's own fall is - but unlike that system,
    // which eases toward a single Y resolved once at spawn, this re-resolves real ground height
    // under the orb's CURRENT position every tick, so it lands the instant its trajectory reaches
    // whatever terrain is actually beneath it rather than ever being placed below a mesh it hasn't
    // reached yet (the "orb spawns under the ground" bug this replaces). Removes PopVelocity on
    // landing, same "settled = free to iterate afterward" contract SettlingToGround/
    // GroundSettleSystem already use.
    [Preserve]
    public unsafe class PopMotionSystem : SystemMainThreadFilter<PopMotionSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y);
            FPVector3 velocity = filter.Pop->Velocity;
            velocity.Y -= gravity * f.DeltaTime;

            FPVector3 nextPosition = filter.Transform3D->Position + velocity * f.DeltaTime;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, nextPosition, groundLayerMask, out FP groundY) == true)
            {
                FP restY = groundY + GroundOffsetUtility.ResolveGroundClearance(f, filter.Entity) + filter.GroundOffset->Offset;

                if (nextPosition.Y <= restY)
                {
                    filter.Transform3D->Position = new FPVector3(nextPosition.X, restY, nextPosition.Z);
                    f.Remove<PopVelocity>(filter.Entity);
                    return;
                }
            }

            filter.Transform3D->Position = nextPosition;
            filter.Pop->Velocity = velocity;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public GroundOffset* GroundOffset;
            public PopVelocity* Pop;
        }
    }
}
