namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Boss/Elite-tier equivalent of PlayerFallSystem - confirmed with the user: these two tiers get
    // the same "fall off the level -> take fall damage -> respawn to safety" treatment players
    // already have, rather than being lost/stuck if physics/knockback pushes one off a ledge. Every
    // other tier (Filler/Normal/Specialist/Heavy) is deliberately excluded - losing a disposable
    // enemy to a fall is a non-issue, but a Boss stuck unreachable (or an Elite the Elite-phase
    // encounter-hold in SurvivalProgressionUtility.IsEncounterCleared is still waiting on) would
    // actually break things. A Persistent enemy (EnemyDataAsset.Economy.Persistent, ANY tier) gets
    // the same rescue for the same reason - it's meant to never leave, so losing it to a fall would
    // be exactly as broken as losing a Boss/Elite. Registered right after EnemySystem/BossSystem so
    // it reads this tick's resolved Transform3D.Position, same "run right after movement resolves"
    // placement PlayerFallSystem itself uses relative to KCCSystem/AutoJumpSystem.
    [Preserve]
    public unsafe class EnemyFallSystem : SystemMainThreadFilter<EnemyFallSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);

            if (data == null || (data.Tier != EnemyTier.Boss && data.Tier != EnemyTier.Elite && data.Economy.Persistent == false))
                return;

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (filter.Transform3D->Position.Y >= config.FallDeathHeight)
                return;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == false)
                return;

            FP fallDamage = health->MaxHealth * config.FallDamagePercent;

            // Unlike a player (a "lethal" hit downs them instead of destroying the entity - see
            // PlayerFallSystem's own comment on this being an acceptable rare case), a Boss/Elite
            // has no such safety net - DamageUtility would genuinely kill it. A flat
            // MaxHealth-based fraction can be lethal to one that already took combat damage/
            // knockback before falling, even though it's a small percentage of a full health bar.
            // Confirmed with the user: these two tiers must NEVER actually die from a fall - clamp
            // so at most it's left at 1 HP, never fully depleted, then still respawn normally.
            fallDamage = FPMath.Min(fallDamage, FPMath.Max(FP._0, health->CurrentHealth - FP._1));

            Log.Debug($"[Fall] {filter.Entity} ({data.name}) fell below FallDeathHeight={config.FallDeathHeight} " +
                      $"(Y={filter.Transform3D->Position.Y}) - dealing {fallDamage} fall damage");

            // Bypasses crit/multiplier resolution - same reasoning PlayerFallSystem's own call
            // already documents: no owner, no weapon/skill source to roll against, just a flat
            // fraction of MaxHealth.
            DamageUtility.ApplyDamage(f, filter.Entity, fallDamage, EntityRef.None, bypassOutgoingResolution: true);

            // Should never actually happen now that fallDamage is clamped above - kept as a guard
            // rather than an assumption, same "check Phase, not f.Exists" reasoning as before (a
            // dead enemy lingers in EnemyActionPhase.Dead for EnemyDataAsset.DeathLingerTime rather
            // than being destroyed immediately, see DamageUtility's own enemy-death branch).
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
            {
                Log.Error($"[Fall] {filter.Entity} ({data.name}) died from a fall despite the 1-HP clamp - investigate");
                return;
            }

            FPVector3 respawnPosition = ResolveRespawnPosition(f, filter.Transform3D->Position, data, config);

            filter.Transform3D->Position = respawnPosition;

            // Without this, whatever fall/knockback velocity carried it off the level would just
            // keep driving it straight back off the respawn point. PlayerFallSystem needs the same
            // reset and does it explicitly too - KCC.Teleport does NOT clear velocity on its own
            // (it only sets position/HasTeleported), which this comment used to claim.
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;

            Log.Debug($"[Fall] {filter.Entity} ({data.name}) respawned at {respawnPosition}");
        }

        // Boss respawns at its own sealed arena's first spawn point (BossSpawnPoints[0], or the
        // geometric center if none are authored - see LevelGenerationSystem.
        // ResolveBossSpawnPositions), not the generic nearest-chunk fallback below - during the
        // fight the arena is walled off via BossArenaGate (see RunPhaseUtility.BeginBossEncounter),
        // so respawning it into some nearby chunk instead would strand it outside its own sealed
        // boundary. Neither Elite nor a Persistent enemy (EnemyDataAsset.Economy.Persistent) has an
        // equivalent "home area" to return to - either can be anywhere on the map when it falls - so
        // both reuse the exact same nearest-chunk/inset logic a falling player gets
        // (FallRespawnUtility, shared with PlayerFallSystem).
        //
        // Both branches are re-grounded via the same top-down raycast BeginBossEncounter itself
        // uses (EnemyMovementUtility.TryFindGroundHeight) - neither a Boss Arena marker's own
        // authored pivot nor FallRespawnUtility's chunk-bounds inset is guaranteed to land exactly
        // on the real walkable floor (a chunk's Transform3D.Position.Y is its authored origin, not
        // necessarily ground height at every point inside its footprint). A player never needed this
        // fix - their own KCC re-grounds every tick regardless of where PlayerFallSystem's
        // KCC.Teleport lands them - but an enemy has no such correction, so leaving this unresolved
        // was respawning it floating above (or clipped below) the real floor, reading as still
        // mid-fall rather than landed.
        private static FPVector3 ResolveRespawnPosition(Frame f, FPVector3 fromPosition, EnemyDataAsset data, LevelConfig config)
        {
            FPVector3 respawnPosition;

            if (data.Tier == EnemyTier.Boss && LevelGenerationSystem.TryFindBossArenaChunk(f, out EntityRef bossChunk) == true)
            {
                List<FPVector3> spawnPositions = new List<FPVector3>();
                LevelGenerationSystem.ResolveBossSpawnPositions(f, bossChunk, spawnPositions);
                respawnPosition = spawnPositions[0];
            }
            else
            {
                if (data.Tier == EnemyTier.Boss)
                    Log.Error("[Fall] Boss fell but no Boss Arena chunk was found - falling back to the generic nearest-chunk respawn");

                respawnPosition = FallRespawnUtility.ResolveNearestChunkRespawnPosition(f, fromPosition, config);
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, respawnPosition, groundLayerMask, out FP groundY) == true)
                respawnPosition.Y = groundY;

            return respawnPosition;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public Transform3D* Transform3D;
            public PhysicsBody3D* PhysicsBody3D;
        }
    }
}
