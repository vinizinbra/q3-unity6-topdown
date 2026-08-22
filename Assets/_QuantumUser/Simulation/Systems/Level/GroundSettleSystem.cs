namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // The gravity half of GroundOffset (see GroundOffset.qtn): while an entity's GroundOffset is
    // Enabled, this re-resolves the real ground underneath it EVERY tick and moves
    // Transform3D.Position.Y toward its resting height - descending entities (a Sentry dropping onto
    // the ground) accelerate like real gravity, scaled off the project's own shared
    // f.SimulationConfig.Physics.Gravity by GroundOffset.FallGravityMultiplier (the same "multiplier
    // on the one true gravity" idiom PhysicsBody3D.GravityScale already uses for real dynamic
    // bodies), while ascending ones (a Vortex rising into its hover height) move at a flat
    // GroundOffset.FloatSpeed instead, since floating up shouldn't read as a launch.
    //
    // Clears Enabled the instant it arrives. That's the whole cost story: an entity that has landed
    // is one bool check per tick, and only entities still actually moving pay for a raycast - the
    // same "settled = free to iterate afterward" contract PopVelocity/PopMotionSystem uses for an
    // orb's arc.
    [Preserve]
    public unsafe class GroundSettleSystem : SystemMainThreadFilter<GroundSettleSystem.Filter>
    {
        private static readonly FP ArrivalTolerance = FP._0_01;

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.GroundOffset->Enabled == false)
                return;

            // PopMotionSystem owns a popped orb's position for as long as its ballistic arc is
            // running - it does its own per-tick ground resolve against the same GroundOffset.Offset
            // and clears Enabled itself on landing. Two systems integrating the same Y would fight.
            if (f.Has<PopVelocity>(filter.Entity) == true)
                return;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            // Nothing underneath (yet) - hold position instead of falling. This is what lets a
            // map-baked prop sit patiently through frame 0, when the procedural level genuinely does
            // not exist beneath it, and start falling the moment it does (see GroundOffset.qtn).
            // Deliberately silent rather than an error: a prop authored out over a hole would
            // otherwise spam every tick, and falling into the void is strictly worse than hovering.
            // Actors have their own real answer to this (PlayerFallSystem/EnemyFallSystem).
            if (EnemyMovementUtility.TryFindGroundHeight(f, filter.Transform3D->Position, groundLayerMask, out FP groundY, filter.Entity) == false)
                return;

            FP targetY = groundY + GroundOffsetUtility.ResolveGroundClearance(f, filter.Entity) + filter.GroundOffset->Offset;
            FP currentY = filter.Transform3D->Position.Y;
            FP delta = targetY - currentY;

            if (FPMath.Abs(delta) <= ArrivalTolerance)
            {
                Settle(ref filter, targetY);
                return;
            }

            FP step;

            if (delta < FP._0)
            {
                FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y) * filter.GroundOffset->FallGravityMultiplier;
                filter.GroundOffset->FallVelocity += gravity * f.DeltaTime;
                step = filter.GroundOffset->FallVelocity * f.DeltaTime;
            }
            else
            {
                // Rising: drop any fall speed banked on the way down, so an entity that overshot and
                // is now climbing back doesn't carry a stale velocity into its next descent.
                filter.GroundOffset->FallVelocity = FP._0;
                step = filter.GroundOffset->FloatSpeed * f.DeltaTime;
            }

            // step <= 0 covers the "0 = snap instead of easing" contract on both GroundOffset rate
            // fields - FallGravityMultiplier/FloatSpeed left at 0 never builds enough step to move at
            // all, so arriving here would otherwise stall forever instead of settling.
            if (step <= FP._0 || step >= FPMath.Abs(delta))
            {
                Settle(ref filter, targetY);
                return;
            }

            FPVector3 position = filter.Transform3D->Position;
            filter.Transform3D->Position = new FPVector3(position.X, currentY + FPMath.Sign(delta) * step, position.Z);
        }

        private static void Settle(ref Filter filter, FP targetY)
        {
            FPVector3 position = filter.Transform3D->Position;
            filter.Transform3D->Position = new FPVector3(position.X, targetY, position.Z);
            filter.GroundOffset->FallVelocity = FP._0;
            filter.GroundOffset->Enabled = false;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public GroundOffset* GroundOffset;
        }
    }
}
