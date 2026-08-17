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
    // actually break things. Registered right after EnemySystem/BossSystem so it reads this tick's
    // resolved Transform3D.Position, same "run right after movement resolves" placement
    // PlayerFallSystem itself uses relative to KCCSystem/AutoJumpSystem.
    [Preserve]
    public unsafe class EnemyFallSystem : SystemMainThreadFilter<EnemyFallSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);

            if (data == null || (data.Tier != EnemyTier.Boss && data.Tier != EnemyTier.Elite))
                return;

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (filter.Transform3D->Position.Y >= config.FallDeathHeight)
                return;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == false)
                return;

            FP fallDamage = health->MaxHealth * config.FallDamagePercent;

            Log.Debug($"[Fall] {filter.Entity} ({data.name}) fell below FallDeathHeight={config.FallDeathHeight} " +
                      $"(Y={filter.Transform3D->Position.Y}) - dealing {fallDamage} fall damage");

            // Bypasses crit/multiplier resolution - same reasoning PlayerFallSystem's own call
            // already documents: no owner, no weapon/skill source to roll against, just a flat
            // fraction of MaxHealth.
            DamageUtility.ApplyDamage(f, filter.Entity, fallDamage, EntityRef.None, bypassOutgoingResolution: true);

            // The fall was lethal - unlike a player, a dead enemy isn't destroyed immediately (it
            // lingers in EnemyActionPhase.Dead for EnemyDataAsset.DeathLingerTime, see
            // DamageUtility's own enemy-death branch), so this checks Phase rather than f.Exists,
            // same as PlayerFallSystem's f.Exists check but for the enemy-specific death shape. No
            // respawn to do either way.
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            FPVector3 respawnPosition = ResolveRespawnPosition(f, filter.Transform3D->Position, data, config);

            filter.Transform3D->Position = respawnPosition;

            // Teleport() on a player's KCC also zeroes its velocity sources for the same reason -
            // without this, whatever fall/knockback velocity carried it off the level would just
            // keep driving it straight back off the respawn point.
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;

            Log.Debug($"[Fall] {filter.Entity} ({data.name}) respawned at {respawnPosition}");
        }

        // Boss respawns at its own sealed arena's first spawn point (BossSpawnPoints[0], or the
        // geometric center if none are authored - see LevelGenerationSystem.
        // ResolveBossSpawnPositions), not the generic nearest-chunk fallback below - during the
        // fight the arena is walled off via BossArenaGate (see RunPhaseUtility.BeginBossEncounter),
        // so respawning it into some nearby chunk instead would strand it outside its own sealed
        // boundary. Same top-down ground-height correction BeginBossEncounter itself applies, for
        // the same reason (a chunk's raw authored pivot/marker Y isn't necessarily the real
        // walkable floor). Elite has no equivalent "home area" to return to - it can be anywhere on
        // the map when it falls - so it reuses the exact same nearest-chunk/inset logic a falling
        // player gets (FallRespawnUtility, shared with PlayerFallSystem).
        private static FPVector3 ResolveRespawnPosition(Frame f, FPVector3 fromPosition, EnemyDataAsset data, LevelConfig config)
        {
            if (data.Tier == EnemyTier.Boss)
            {
                if (LevelGenerationSystem.TryFindBossArenaChunk(f, out EntityRef bossChunk) == true)
                {
                    List<FPVector3> spawnPositions = new List<FPVector3>();
                    LevelGenerationSystem.ResolveBossSpawnPositions(f, bossChunk, spawnPositions);

                    FPVector3 respawnPosition = spawnPositions[0];
                    int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

                    if (EnemyMovementUtility.TryFindGroundHeight(f, respawnPosition, groundLayerMask, out FP groundY) == true)
                        respawnPosition.Y = groundY;

                    return respawnPosition;
                }

                Log.Error("[Fall] Boss fell but no Boss Arena chunk was found - falling back to the generic nearest-chunk respawn");
            }

            return FallRespawnUtility.ResolveNearestChunkRespawnPosition(f, fromPosition, config);
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
