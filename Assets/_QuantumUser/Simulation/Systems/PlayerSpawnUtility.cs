namespace Quantum
{
    using Photon.Deterministic;

    // Shared entry point for actually creating a player's avatar - used both by PlayerInitSystem
    // (for anyone joining after the level already exists) and LevelGenerationSystem (to spawn
    // anyone who joined before the level was ready, deferred rather than positioned then corrected).
    public static unsafe class PlayerSpawnUtility
    {
        // Chunk colliders created by LevelGenerationSystem this frame may not be registered in the
        // physics broadphase until the next physics step - spawning a player (gravity/KCC already
        // active) immediately can let them fall through geometry that technically exists but isn't
        // collidable yet. Waiting this long after LevelGenerated flips true gives it time to settle.
        private static readonly FP SpawnDelaySeconds = FP._1;

        // All players share the same PlayerSpawnPosition - spread them around it so they don't
        // spawn stacked on top of each other.
        private static readonly FP SpawnOffsetDistance = FP._1 + FP._0_50;

        public static bool IsReadyToSpawn(Frame f)
        {
            return f.Global->LevelGenerated && f.Global->TimeSinceLevelGenerated >= SpawnDelaySeconds;
        }

        public static void Spawn(Frame f, PlayerRef player)
        {
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            EntityRef entity = f.Create(runtimePlayer.PlayerAvatar);

            if (f.Unsafe.TryGetPointer<PlayerLink>(entity, out var playerLink))
            {
                playerLink->Player = player;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform))
            {
                FP spawnHeight = f.FindAsset(f.RuntimeConfig.LevelConfig).PlayerSpawnHeight;
                FPVector3 offset = GetSpawnOffset((int)player, f.PlayerCount);
                transform->Position = f.Global->PlayerSpawnPosition + offset + FPVector3.Up * spawnHeight;
                Log.Debug($"[LevelGen] spawned player {player} at {transform->Position}");
            }

            // Meta-progression, carried in from outside this match (see RuntimePlayer.WeaponLevel's
            // own comment / MatchMakingConfig.StartRunner) - overrides the fresh 0
            // CharacterSystem.OnEntityPrototypeMaterialized already seeded a moment ago inside
            // f.Create above, same "override right after creation" idiom the Transform3D write
            // above uses for spawn position. Read here (not from CharacterSystem itself) because
            // PlayerLink.Player - and therefore which RuntimePlayer this entity even belongs to -
            // isn't set until the block above, after f.Create already returned.
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats))
            {
                stats->WeaponTalentLevel = runtimePlayer.WeaponLevel;
            }

            // Skill stacks are NOT initialized here - SkillSystem.EnsureInitialized does it lazily
            // on first Update instead, so it's correct regardless of how the entity came to exist
            // (this dynamic spawn path, or a player placed directly in a scene for testing, which
            // never runs through Spawn() at all - see that method's own comment).
        }

        // Spreads players evenly around a circle centered on PlayerSpawnPosition, SpawnOffsetDistance
        // from the center, so they don't land on top of each other.
        private static FPVector3 GetSpawnOffset(int playerIndex, int playerCount)
        {
            FP anglePerPlayer = playerCount > 0 ? FP._360 / playerCount : FP._0;
            FP angleRad = playerIndex * anglePerPlayer * FP.Deg2Rad;

            FPMath.SinCos(angleRad, out FP sin, out FP cos);
            return new FPVector3(cos, FP._0, sin) * SpawnOffsetDistance;
        }

        public static bool HasSpawned(Frame f, PlayerRef player)
        {
            var filtered = f.Filter<PlayerLink>();
            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                if (playerLink.Player == player)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
