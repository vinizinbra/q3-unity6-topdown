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
            var input = f.GetPlayerInput(filter.PlayerLink->Player);
            if (isGrounded == true && canJump == true && input->Jump.WasPressed == true)
            {
                DoJump(f, filter.Entity, filter.KCC, filter.PlayerMovement, data);
            }
        }

        private static void DoJump(Frame f, EntityRef entity, KCC* kcc, PlayerMovement* movement, MovementDataAsset data)
        {
            kcc->Jump(FPVector3.Up * data.JumpVelocity);
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
