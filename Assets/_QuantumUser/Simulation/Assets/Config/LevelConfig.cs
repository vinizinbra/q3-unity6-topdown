namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // One interchangeable variant inside a ChunkPoolEntry - the prototype plus how often it should
    // be picked relative to its siblings. Weight mirrors EnemyGroupConfig.Weight's own convention:
    // it only biases how often an already-valid variant wins, and <= 0 soft-disables that variant
    // (a designer can mute a prototype without removing it from the list). The one deliberate
    // difference is the all-zero case - Unity zero-inits a freshly added array element, so an entry
    // whose variants are ALL <= 0 (an unmigrated/unauthored list) falls back to the old uniform pick
    // rather than silently placing nothing. See LevelGenerationSystem.PickVariant.
    [Serializable]
    public struct ChunkPrototypeVariant
    {
        public AssetRef<EntityPrototype> Prototype;
        public FP Weight;
    }

    // One entry in LevelConfig.ChunkPool - describes a SET of interchangeable chunk prototype
    // variants the generator can place and how many total should end up in a generated level. Each
    // of the Count placements independently rolls one variant out of Prototypes, weighted by that
    // variant's own Weight (via f.RNG, so every client generates the identical layout) - e.g. an
    // Enemy entry with 4 Prototypes and Count 10 places 10 enemy chunks, each one of the 4
    // variants. LobbyStart should have Count 1 (the generator seeds the grid with it); Boss isn't
    // listed here at all - it's a fixed, hand-placed chunk (BossArena) with its own pre-baked
    // navmesh that the generator discovers in the scene and grows the rest of the level around,
    // rather than something it spawns itself.
    // Footprint size isn't here - LevelGenerationSystem reads it straight off the entity's own
    // baked Chunk component right after f.Create, so a prefab's size only has to be authored once.
    [Serializable]
    public struct ChunkPoolEntry
    {
        public ChunkType Type;

        // Interchangeable variant prototypes for this entry - one is picked per placed instance,
        // weighted by each variant's own Weight. A single-element array reproduces the old "one
        // prototype per entry" behavior regardless of what that entry's Weight is.
        public ChunkPrototypeVariant[] Prototypes;

        public Int32 Count;

        // If true, LevelGenerationSystem treats every instance of this entry as required: it gets
        // far more placement attempts than an optional entry, and a failure to place it after that
        // is logged as an Error (not the usual Debug) so a broken layout can't slip by unnoticed.
        public bool MustHave;
    }

    public class LevelConfig : AssetObject
    {
        public FP CellSize = 10;

        // Bounds the procedural area so generation can't run away indefinitely - not tied to any
        // navmesh bake (regular chunks don't use navmesh; see BossArena for the one area that does).
        public Int32 GridWidth = 20;
        public Int32 GridDepth = 20;

        // Height above the floor the player spawns at - a small drop onto the floor instead of
        // spawning exactly at Y=0, so they don't spawn clipped into the LobbyStart chunk's own geometry.
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

        // Delay (seconds) between a fall being detected (damage applied, FallDeathTriggered VFX
        // fired) and the actual respawn teleport - PlayerFallSystem/EnemyFallSystem's own
        // FallRespawnTimer counts this down, so the fall reads before the character snaps back
        // instead of an instant teleport in the same tick.
        public FP FallRespawnDelay = 1;

        // Minimum distance PlayerFallSystem's automatic (non-baked) respawn fallback keeps from
        // its chunk's own footprint boundary - only applies when that chunk has no hand-authored
        // Chunk.RespawnPoint.
        public FP FallRespawnEdgeMargin = 2;

        // A candidate placement touching an already-placed chunk with a straight overlap shorter
        // than this (in cells) is rejected - prevents a chunk from attaching via a razor-thin
        // sliver (e.g. a single-cell-wide doorway) instead of a real, walkable connection. 1
        // reproduces the old unrestricted behavior.
        public Int32 MinConnectionWidthCells = 2;

        public ChunkPoolEntry[] ChunkPool;

        // How many chunk requests LevelGenerationSystem places per simulation tick. Generation used
        // to run start-to-finish inside one tick, which froze the client for as long as it took to
        // place every chunk AND for the View to instantiate all of their prefabs in the same Unity
        // frame - long enough to read as a hard hang, and long enough to risk stalling the network
        // pump into a disconnect. Raise it to generate faster at the cost of a heavier per-tick
        // spike; <= 0 is clamped to 1 rather than meaning "all at once", so a misauthored 0 can't
        // reintroduce the freeze. Nothing spawns players any earlier either way -
        // PlayerSpawnUtility.IsReadyToSpawn still waits for the final tick's LevelGenerated flip.
        public Int32 ChunksPerGenerationTick = 1;

        // Optional - GrowLevel's frontier-based placement has no way to guarantee full coverage,
        // so it can leave scattered single-cell pockets fully enclosed by chunks. If assigned,
        // LevelGenerationSystem.FillInnerGaps spawns one of these at every such cell once
        // generation finishes. Must have a Transform3D (its Position is set on spawn) and should
        // be sized to fill exactly one CellSize x CellSize cell (e.g. a 2x2 wall prefab when
        // CellSize=2). Left unassigned, inner gaps are simply left as open floor, same as today.
        public AssetRef<EntityPrototype> GapFillerPrototype;
    }
}
