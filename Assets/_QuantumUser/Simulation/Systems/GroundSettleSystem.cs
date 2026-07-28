namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Eases a spawned entity's Transform3D.Position.Y from wherever SpawnedEntitySpawner placed it
    // toward its resolved resting height (SettlingToGround.TargetY, from GroundOffset.Offset) -
    // descending entities (a Sentry dropping onto the ground) accelerate like real gravity, scaled
    // off the project's own shared f.SimulationConfig.Physics.Gravity by
    // GroundOffset.FallGravityMultiplier (same "multiplier on the one true gravity" idiom
    // PhysicsBody3D.GravityScale already uses for real dynamic bodies), while ascending ones (a
    // Vortex rising into its hover height) move at a flat GroundOffset.FloatSpeed instead, since
    // floating up shouldn't read as a launch. Removes SettlingToGround the instant it arrives, so a
    // settled entity is free to iterate afterward and this system only ever costs anything for
    // entities still mid-settle.
    [Preserve]
    public unsafe class GroundSettleSystem : SystemMainThreadFilter<GroundSettleSystem.Filter>
    {
        private static readonly FP ArrivalTolerance = FP._0_01;

        public override void Update(Frame f, ref Filter filter)
        {
            FP currentY = filter.Transform3D->Position.Y;
            FP targetY = filter.Settling->TargetY;
            FP delta = targetY - currentY;

            if (FPMath.Abs(delta) <= ArrivalTolerance)
            {
                Settle(f, ref filter, targetY);
                return;
            }

            FP step;

            if (delta < FP._0)
            {
                FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y) * filter.GroundOffset->FallGravityMultiplier;
                filter.Settling->FallVelocity += gravity * f.DeltaTime;
                step = filter.Settling->FallVelocity * f.DeltaTime;
            }
            else
            {
                step = filter.GroundOffset->FloatSpeed * f.DeltaTime;
            }

            // step <= 0 covers the "0 = snap instead of easing" contract on both GroundOffset rate
            // fields - FallGravityMultiplier/FloatSpeed left at 0 never builds enough step to move
            // at all, so arriving here would otherwise stall forever instead of settling.
            if (step <= FP._0 || step >= FPMath.Abs(delta))
            {
                Settle(f, ref filter, targetY);
                return;
            }

            FPVector3 position = filter.Transform3D->Position;
            filter.Transform3D->Position = new FPVector3(position.X, currentY + FPMath.Sign(delta) * step, position.Z);
        }

        private static void Settle(Frame f, ref Filter filter, FP targetY)
        {
            FPVector3 position = filter.Transform3D->Position;
            filter.Transform3D->Position = new FPVector3(position.X, targetY, position.Z);
            f.Remove<SettlingToGround>(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public GroundOffset* GroundOffset;
            public SettlingToGround* Settling;
        }
    }
}
