namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Runs after KCCSystem, reacting to this tick's freshly-resolved grounded state.
    // Auto-hop and auto-mantle are predictively detected in PlayerMovementProcessor instead
    // (so the jump lands the same tick it's detected) - the IsOnEdge check here is just a
    // cheap fallback in case that predictive check misses. Also handles the manual jump button
    // and the landing/cooldown bookkeeping both rely on.
    [Preserve]
    public unsafe class AutoJumpSystem : SystemMainThreadFilter<AutoJumpSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            MovementDataAsset data = f.FindAsset(filter.PlayerMovement->MovementData);

            bool wasGrounded = filter.KCC->Data.WasGrounded;
            bool isGrounded = filter.KCC->Data.IsGrounded;
            bool isOnEdge = filter.KCC->Data.IsOnEdge;

            // Captured before LastGroundedPosition gets overwritten below - this tick's landing
            // branch needs the pre-jump takeoff height to compute fallDistance.
            FP previousGroundedY = filter.PlayerMovement->LastGroundedPosition.Y;

            // Tracked here rather than a dedicated system since this already reads IsGrounded
            // every tick right after KCCSystem resolves it. PlayerFallSystem respawns here.
            if (isGrounded == true)
            {
                filter.PlayerMovement->LastGroundedPosition = filter.KCC->Position;
            }

            if (wasGrounded == false && isGrounded == true)
            {
                filter.PlayerMovement->HasAirJumped = false;

                // Restart the cooldown on landing (not just at jump time) - airtime can easily
                // exceed JumpCooldownTime, which would otherwise leave zero grace period against
                // an immediate re-trigger the instant the character touches down.
                filter.PlayerMovement->JumpCooldownTimer = data.JumpCooldownTime;

                // Generic landing hook (see PlayerMovement.qtn) - Brute's Groundbreaker Ascension is
                // the only reaction gated on this today, everyone else is unaffected.
                //
                // Clamped at 0, so landing HIGHER than takeoff (an auto-mantle up a ledge) reports no
                // fall at all rather than a negative one.
                FP fallDistance = FPMath.Max(FP._0, previousGroundedY - filter.KCC->Position.Y);
                f.Signals.OnPlayerLanded(filter.Entity, fallDistance, filter.PlayerMovement->AirborneSource);

                // Reset AFTER the signal - the next stretch of airtime is a plain fall unless
                // something (a jump, a launch) explicitly claims it. See LandingSource.
                filter.PlayerMovement->AirborneSource = LandingSource.Fall;
            }

            if (filter.PlayerMovement->JumpCooldownTimer > FP._0)
            {
                filter.PlayerMovement->JumpCooldownTimer -= f.DeltaTime;
            }

            bool canJump = filter.PlayerMovement->HasAirJumped == false && filter.PlayerMovement->JumpCooldownTimer <= FP._0;

            // Fallback: PlayerMovementProcessor should already have caught this predictively.
            if (isOnEdge == true && canJump == true)
            {
                DoJump(f, filter.Entity, filter.KCC, filter.PlayerMovement, data);
                return;
            }

            // Manual jump button, for testing without needing to trigger auto-mantle/hop.
            var input = PlayerInputUtility.Resolve(f, filter.Entity, filter.PlayerLink);
            if (isGrounded == true && canJump == true && input->Jump.WasPressed == true)
            {
                DoJump(f, filter.Entity, filter.KCC, filter.PlayerMovement, data);
            }
        }

        private static void DoJump(Frame f, EntityRef entity, KCC* kcc, PlayerMovement* movement, MovementDataAsset data)
        {
            kcc->Jump(FPVector3.Up * data.JumpVelocity);
            movement->AirborneSource = LandingSource.Jump;
            movement->HasAirJumped = true;
            movement->JumpCooldownTimer = data.JumpCooldownTime;
            f.Events.PlayerJumped(entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public KCC* KCC;
            public PlayerLink* PlayerLink;
            public PlayerMovement* PlayerMovement;
        }
    }
}
