namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Runs after AutoJumpSystem so LastGroundedPosition reflects this tick's freshly-resolved
    // grounded state before checking whether the player has fallen off the level.
    [Preserve]
    public unsafe class PlayerFallSystem : SystemMainThreadFilter<PlayerFallSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (filter.KCC->Position.Y >= config.FallDeathHeight)
                return;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == false)
                return;

            FP fallDamage = health->MaxHealth * config.FallDamagePercent;

            Log.Debug($"[Fall] {filter.Entity} fell below FallDeathHeight={config.FallDeathHeight} " +
                      $"(Y={filter.KCC->Position.Y}) - dealing {fallDamage} fall damage");

            // Bypasses crit/multiplier resolution - there's no owner and no weapon/skill source to
            // roll against, just a flat fraction of MaxHealth.
            DamageUtility.ApplyDamage(f, filter.Entity, fallDamage, EntityRef.None, bypassOutgoingResolution: true);

            // The fall was lethal (rare - only when already critically low) - DamageUtility already
            // destroyed the entity above, same as any other lethal hit today. No respawn to do.
            if (f.Exists(filter.Entity) == false)
                return;

            FPVector3 respawnPosition = ResolveRespawnPosition(f, filter.PlayerMovement->LastGroundedPosition, config);
            filter.KCC->Teleport(f, respawnPosition);

            // KCC.Teleport moves the character but does NOT clear its velocity - it only sets
            // DesiredPosition/TargetPosition/HasTeleported and the stepping-up/ground-snapping
            // flags. Without this the player arrives at the respawn point still carrying every bit
            // of the downward speed the fall built up, which is what turned one fall into a loop:
            // respawn -> immediately fall again -> take fall damage again -> respawn faster, since
            // DynamicVelocity was never reset and just kept accumulating Gravity * dt.
            // PlayerMovementProcessor.ClampFallSpeed now bounds how fast that can get, but a
            // respawn should land you standing, not already falling at terminal velocity. Same
            // three-line reset RunPhaseUtility.TeleportPlayersToBossArena already does after its
            // own KCC.Teleport, for exactly the same reason.
            filter.KCC->SetKinematicVelocity(FPVector3.Zero);
            filter.KCC->SetDynamicVelocity(FPVector3.Zero);
            filter.KCC->SetExternalImpulse(FPVector3.Zero);

            Log.Debug($"[Fall] {filter.Entity} respawned at {respawnPosition}");

            f.Events.PlayerRespawned(filter.Entity, respawnPosition);
        }

        // Thin wrapper over the shared FallRespawnUtility (also used by EnemyFallSystem for
        // Boss/Elite falls) - "last grounded position" is by definition right at the edge a player
        // walked off, so it's the right "fromPosition" to inset away from/find the nearest chunk
        // around.
        private static FPVector3 ResolveRespawnPosition(Frame f, FPVector3 lastGroundedPosition, LevelConfig config)
        {
            return FallRespawnUtility.ResolveNearestChunkRespawnPosition(f, lastGroundedPosition, config);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public KCC* KCC;
            public PlayerMovement* PlayerMovement;
        }
    }
}
