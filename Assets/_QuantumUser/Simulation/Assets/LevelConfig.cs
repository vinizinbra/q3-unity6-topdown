namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // One entry in LevelConfig.ChunkPool - describes a chunk prototype the generator can place
    // and how many of it should end up in a generated level. Start should have Count 1 (the
    // generator seeds the grid with it); Boss isn't listed here at all - it's a fixed, hand-placed
    // chunk (BossArena) with its own pre-baked navmesh that the generator discovers in the scene
    // and grows the rest of the level around, rather than something it spawns itself.
    // Footprint size isn't here - LevelGenerationSystem reads it straight off the entity's own
    // baked Chunk component right after f.Create, so a prefab's size only has to be authored once.
    [Serializable]
    public struct ChunkPoolEntry
    {
        public ChunkType Type;
        public AssetRef<EntityPrototype> Prototype;
        public Int32 Count;
    }

    public class LevelConfig : AssetObject
    {
        public FP CellSize = 10;

        // Bounds the procedural area so generation can't run away indefinitely - not tied to any
        // navmesh bake (regular chunks don't use navmesh; see BossArena for the one area that does).
        public Int32 GridWidth = 20;
        public Int32 GridDepth = 20;

        // Height above the floor the player spawns at - a small drop onto the floor instead of
        // spawning exactly at Y=0, so they don't spawn clipped into the Start chunk's own geometry.
        public FP PlayerSpawnHeight = 2;

        // World-Y threshold below which a character is considered to have fallen off the level -
        // floor is baked at Y=0 (see LevelGenerationSystem.FootprintCenterToWorld), so a single
        // flat threshold covers every chunk without needing per-chunk bounds.
        public FP FallDeathHeight = -10;

        // Added on top of PlayerMovement.LastGroundedPosition on respawn, so the player doesn't
        // land clipped into the floor they just fell through.
        public FP FallRespawnHeightOffset = 2;

        // Fraction of MaxHealth a player takes when they fall off the level.
        public FP FallDamagePercent = FP._0_10;

        public ChunkPoolEntry[] ChunkPool;
    }
}
