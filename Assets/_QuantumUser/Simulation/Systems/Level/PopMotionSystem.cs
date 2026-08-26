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
    // landing, same "settled = free to iterate afterward" contract GroundOffset.Enabled/
    // GroundSettleSystem already use.
    [Preserve]
    public unsafe class PopMotionSystem : SystemMainThreadFilter<PopMotionSystem.Filter>
    {
        // How much higher than its own origin floor an orb may still settle on. Covers the ordinary
        // unevenness of real terrain - a ramp, a kerb, a slightly raised slab - without letting a drop
        // reach a genuine platform above it. Deliberately generous next to MovementDataAsset
        // .MaxLedgeHeight (1, the tallest step a player auto-mantles): anything a player would have to
        // deliberately climb is exactly what this exists to keep coins off.
        //
        // A constant rather than an authored field on purpose - this is a reachability guard, not a
        // balance knob, so it is never TUNED per drop type. It is however opt-out-able per drop
        // (PopVelocity.CanLandHigher): a dropped Signature Accessory deliberately may land above you,
        // because going and getting it back is that mechanic's whole point - docs/accessory-guard.md.
        private static readonly FP MaxRiseAboveOrigin = FP._0_50;

        public override void Update(Frame f, ref Filter filter)
        {
            FP gravity = FPMath.Abs(f.SimulationConfig.Physics.Gravity.Y);
            FPVector3 velocity = filter.Pop->Velocity;
            velocity.Y -= gravity * f.DeltaTime;

            FPVector3 position = filter.Transform3D->Position;
            FPVector3 nextPosition = position + velocity * f.DeltaTime;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            // Refuse to carry the orb onto anything meaningfully higher than the floor it dropped from
            // (see PopVelocity.OriginGroundY). Without this the arc happily clears a raised platform's
            // lip, the landing check below finds that platform's surface under the orb, and the coin
            // settles up there - reachable only by a detour the player has no reason to take.
            //
            // Modelled as a bump, not as ignoring the surface: horizontal travel is dropped while the
            // vertical component keeps integrating, so the orb stops dead against the platform's edge
            // and falls straight down onto its own floor. Ignoring the raised ground instead would let
            // the orb sail THROUGH the platform and settle underneath it, which is strictly worse.
            //
            // Only climbing is blocked. Ground lower than the origin passes straight through here, so
            // an enemy killed on a ledge still scatters coins down off it as before.
            if (filter.Pop->CanLandHigher == false
                && EnemyMovementUtility.TryFindGroundHeight(f, nextPosition, groundLayerMask, out FP aheadGroundY, filter.Entity) == true
                && aheadGroundY > filter.Pop->OriginGroundY + MaxRiseAboveOrigin)
            {
                velocity.X = FP._0;
                velocity.Z = FP._0;
                nextPosition = new FPVector3(position.X, position.Y + velocity.Y * f.DeltaTime, position.Z);
            }

            if (EnemyMovementUtility.TryFindGroundHeight(f, nextPosition, groundLayerMask, out FP groundY, filter.Entity) == true)
            {
                FP restY = groundY + GroundOffsetUtility.ResolveGroundClearance(f, filter.Entity) + filter.GroundOffset->Offset;

                if (nextPosition.Y <= restY)
                {
                    filter.Transform3D->Position = new FPVector3(nextPosition.X, restY, nextPosition.Z);
                    f.Remove<PopVelocity>(filter.Entity);

                    // The orb is exactly at its resting height now, so hand GroundSettleSystem an
                    // already-settled entity rather than letting it re-arm and re-raycast one more
                    // time for a move of zero. GroundSettleSystem skips anything still carrying
                    // PopVelocity, so this is the one moment the two systems have to agree.
                    filter.GroundOffset->Enabled = false;
                    filter.GroundOffset->FallVelocity = FP._0;
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
