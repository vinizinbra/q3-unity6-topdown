namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Generates the level out of LevelConfig.ChunkPool, spread over consecutive ticks
    // (LevelConfig.ChunksPerGenerationTick at a time) rather than all inside Frame 0 - see
    // StepGeneration for why and how. Any Chunk already in the world (e.g. a hand-placed BossArena
    // with its own pre-baked navmesh) seeds the grid; everything else is placed by f.Create around
    // it, so every client in the match generates the identical layout from the same f.RNG sequence.
    // Keeps running every frame afterward (rather than early-returning for good) to hold off
    // spawning players until PlayerSpawnUtility.IsReadyToSpawn - see its comment for why.
    [Preserve]
    public unsafe class LevelGenerationSystem : SystemMainThread
    {
        private struct PlacedChunk
        {
            public EntityRef Entity;
            public ChunkType Type;
            public int OriginX;
            public int OriginZ;
            public int Width;
            public int Depth;
            public ChunkConnectionSide AllowedConnectionSides;
            public ChunkTypeMask ForbiddenNeighbors;
        }

        // Footprint isn't stored here - it's read from the entity's own baked Chunk component
        // right after f.Create (see TryPlaceRequest), so LevelConfig never has to duplicate a
        // size that's already authored on the chunk prefab itself.
        private struct ChunkRequest
        {
            public ChunkType Type;
            public AssetRef<EntityPrototype> Prototype;
            public bool MustHave;
        }

        public override void Update(Frame f)
        {
            if (f.Global->LevelGenerated == false)
            {
                StepGeneration(f);
                return;
            }

            bool wasReady = PlayerSpawnUtility.IsReadyToSpawn(f);
            f.Global->TimeSinceLevelGenerated += f.DeltaTime;
            bool isReady = PlayerSpawnUtility.IsReadyToSpawn(f);

            if (isReady == false)
            {
                return;
            }

            if (wasReady == false)
            {
                Log.Debug($"[LevelGen] ready to spawn players now - f.PlayerCount={f.MaxPlayerCount}");
            }

            SpawnPendingPlayers(f);
        }

        // One tick's worth of generation. This used to be a single GenerateLevel call that ran
        // start-to-finish inside Frame 0 - it placed every chunk, and (far more expensively) left
        // the View to instantiate every chunk prefab in the same Unity frame. That read as a hard
        // hang on the client, and a hang that long can stall the main thread past a connection
        // timeout and drop players out of the match. It now places at most
        // LevelConfig.ChunksPerGenerationTick requests per tick and only flips
        // f.Global->LevelGenerated on the FINAL tick - which is what PlayerSpawnUtility.IsReadyToSpawn
        // (and therefore hero spawning) already gates on, so heroes still never appear before the
        // level is finished.
        //
        // Nothing is cached on the system between ticks, deliberately: a Quantum system has to stay
        // stateless because rollback re-simulates ticks, so anything remembered in a field would
        // desync. Instead each tick re-derives its working state:
        //   - occupied / neighborAllowedSides / placed are rebuilt from the Chunk entities that
        //     actually exist in the frame. SeedFromExistingChunks already did exactly this for the
        //     hand-placed Boss Arena; it now also picks up everything earlier ticks placed, and
        //     CommitPlacement's grid<->world math round-trips exactly, so the rebuilt grid is
        //     identical to the one the old single-tick pass carried in locals.
        //   - the shuffled request bag is the one thing NOT re-derivable from the world, so rather
        //     than storing it in frame state, the first tick rolls Global.LevelGenSeed and every
        //     tick rebuilds the identical bag from a private RNGSession seeded with it. Only the
        //     cursor into that bag is persisted.
        private void StepGeneration(Frame f)
        {
            if (f.RuntimeConfig.LevelConfig.Id.IsValid == false)
            {
                Log.Error("[LevelGen] no LevelConfig assigned on RuntimeConfig - skipping procedural generation, player will spawn directly in the Boss Arena instead of a LobbyStart chunk");
                SpawnAtBossArenaDirectly(f);
                f.Global->LevelGenerated = true;
                return;
            }

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);
            bool firstStep = f.Global->LevelGenStarted == false;

            if (firstStep)
            {
                // Rolled off f.RNG (already seeded from RuntimeConfig.Seed), so it varies per run and
                // is identical on every client. Bounded well inside Int32 rather than drawn across the
                // full range - nothing here needs 2^31 distinct layouts, and it keeps the draw clear of
                // any range-arithmetic edge in RNGSession.Next(int, int).
                f.Global->LevelGenSeed = f.RNG->Next(1, 1 << 30);
                f.Global->LevelGenCursor = 0;
                f.Global->LevelGenStarted = true;

                Log.Debug($"[LevelGen] starting - GridWidth={config.GridWidth}, GridDepth={config.GridDepth}, CellSize={config.CellSize}, ChunkPool entries={config.ChunkPool?.Length ?? 0}, ChunksPerGenerationTick={config.ChunksPerGenerationTick}");
            }

            (int gridOriginX, int gridOriginZ) = ComputeGridOrigin(f, config);

            bool[,] occupied = new bool[config.GridWidth, config.GridDepth];
            ChunkConnectionSide[,] neighborAllowedSides = new ChunkConnectionSide[config.GridWidth, config.GridDepth];
            List<PlacedChunk> placed = new List<PlacedChunk>();

            SeedFromExistingChunks(f, config, gridOriginX, gridOriginZ, occupied, neighborAllowedSides, placed, firstStep);

            RNGSession bagRng = new RNGSession(f.Global->LevelGenSeed);
            List<ChunkRequest> bag = BuildShuffledBag(config, ref bagRng, firstStep);

            if (firstStep)
            {
                f.Global->LevelGenTotal = bag.Count;
                Log.Debug($"[LevelGen] grid origin (world cell units) = ({gridOriginX},{gridOriginZ}), seeded from existing chunks - placed={placed.Count}, bag built - requests={bag.Count}");
            }

            // <= 0 is clamped rather than treated as "everything this tick" - a misauthored 0 would
            // otherwise silently restore the exact freeze this whole split exists to avoid.
            int budget = config.ChunksPerGenerationTick > 0 ? config.ChunksPerGenerationTick : 1;

            while (budget > 0 && f.Global->LevelGenCursor < bag.Count)
            {
                TryPlaceRequest(f, config, gridOriginX, gridOriginZ, bag[f.Global->LevelGenCursor], occupied, neighborAllowedSides, placed);
                f.Global->LevelGenCursor++;
                budget--;
            }

            if (f.Global->LevelGenCursor < bag.Count)
            {
                return; // more ticks to go - LevelGenerated stays false, so nobody spawns yet
            }

            Log.Debug($"[LevelGen] grow complete - placed={placed.Count}");

            FillInnerGaps(f, config, gridOriginX, gridOriginZ, occupied);

            VerifyStartNotAdjacentToBoss(placed);

            AssignPlayerSpawnPosition(f, config, gridOriginX, gridOriginZ, placed);

            ComputeChunkConnectivity(f, placed);

            f.Global->LevelGenerated = true;
        }

        // The `occupied` array is a fixed grid indexed from (0,0) - but the Boss Arena's real world
        // position has no reason to fall inside that range on its own (it's just wherever it was
        // placed in the scene). This computes an offset (in world cell units) so subtracting it
        // from a world cell coordinate lands the arena centered inside the array with room to grow
        // on every side, instead of throwing an IndexOutOfRangeException the moment the arena isn't
        // conveniently sitting near world (0,0).
        private (int, int) ComputeGridOrigin(Frame f, LevelConfig config)
        {
            var filtered = f.Filter<Chunk>();
            while (filtered.Next(out EntityRef entity, out Chunk chunk))
            {
                if (chunk.Type != ChunkType.Boss)
                {
                    continue;
                }

                // Chunks are min-corner pivoted and never rotated, so Position IS the min corner and
                // the footprint dimensions map straight to world X/Z.
                FPVector3 minCorner = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                int arenaWorldOriginX = FPMath.RoundToInt(minCorner.X / config.CellSize);
                int arenaWorldOriginZ = FPMath.RoundToInt(minCorner.Z / config.CellSize);
                int arenaWidth = ToCellCount(chunk.ChunkSizeWidth, config);
                int arenaDepth = ToCellCount(chunk.ChunkSizeDepth, config);

                int gridOriginX = arenaWorldOriginX - (config.GridWidth - arenaWidth) / 2;
                int gridOriginZ = arenaWorldOriginZ - (config.GridDepth - arenaDepth) / 2;

                return (gridOriginX, gridOriginZ);
            }

            // No Boss Arena found - fall back to a plain (0,0) origin.
            return (0, 0);
        }

        // Rebuilds the whole working grid from whatever Chunk entities exist right now - originally
        // just the hand-placed Boss Arena, but since generation is spread over ticks (see
        // StepGeneration) this also picks up every chunk earlier ticks placed, which is what lets the
        // system stay stateless between them. logDetails is off on every tick after the first so the
        // per-chunk lines don't repeat once per tick for the whole generation.
        private void SeedFromExistingChunks(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed, bool logDetails)
        {
            var filtered = f.Filter<Chunk>();
            while (filtered.Next(out EntityRef entity, out Chunk chunk))
            {
                // Min-corner pivoted, never rotated - Position IS the min corner.
                FPVector3 minCorner = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                int worldOriginX = FPMath.RoundToInt(minCorner.X / config.CellSize);
                int worldOriginZ = FPMath.RoundToInt(minCorner.Z / config.CellSize);

                PlacedChunk placedChunk = new PlacedChunk
                {
                    Entity = entity,
                    Type = chunk.Type,
                    OriginX = worldOriginX - gridOriginX,
                    OriginZ = worldOriginZ - gridOriginZ,
                    Width = ToCellCount(chunk.ChunkSizeWidth, config),
                    Depth = ToCellCount(chunk.ChunkSizeDepth, config),
                    AllowedConnectionSides = chunk.AllowedConnectionSides,
                    ForbiddenNeighbors = chunk.ForbiddenNeighbors,
                };

                MarkOccupied(occupied, neighborAllowedSides, placedChunk);
                placed.Add(placedChunk);

                if (logDetails)
                {
                    Log.Debug($"[LevelGen] found pre-existing {chunk.Type} chunk {entity} at grid cell ({placedChunk.OriginX},{placedChunk.OriginZ}) (world cell {worldOriginX},{worldOriginZ}), footprint {placedChunk.Width}x{placedChunk.Depth}");
                }
            }
        }

        // Used only when there's no LevelConfig to drive procedural generation at all - finds the
        // Boss Arena directly via f.Filter<Chunk> and sets PlayerSpawnPosition to its footprint
        // center. No grid/CellSize math needed here since nothing is being placed on a grid in
        // this fallback path - ChunkSizeWidth/Depth are used directly as world-space size.
        private void SpawnAtBossArenaDirectly(Frame f)
        {
            var filtered = f.Filter<Chunk>();
            while (filtered.Next(out EntityRef entity, out Chunk chunk))
            {
                if (chunk.Type != ChunkType.Boss)
                {
                    continue;
                }

                // Min-corner pivoted, never rotated - Position IS the min corner, size maps to X/Z.
                FPVector3 minCorner = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                FPVector3 spawnPosition = minCorner + new FPVector3(chunk.ChunkSizeWidth, 0, chunk.ChunkSizeDepth) * FP._0_50;

                f.Global->PlayerSpawnPosition = spawnPosition;
                Log.Debug($"[LevelGen] PlayerSpawnPosition set to {spawnPosition} (Boss Arena at {minCorner}, no LevelConfig)");
                return;
            }

            Log.Error("[LevelGen] no LevelConfig and no Boss Arena chunk found - PlayerSpawnPosition left at default");
        }

        // Reads the already-placed LobbyStart chunk's own world-space footprint back out for
        // LobbyBoundarySystem (is every player outside this footprint yet) - see docs/talents.md.
        // Built from Global.PlayerSpawnPosition (AssignPlayerSpawnPosition's own FootprintCenterToWorld
        // result, pure grid-cell arithmetic) as the center, with the authored Width/Depth as the
        // half-extent - chunks are never rotated, so those map straight to world X/Z.
        internal static bool TryGetLobbyStartBounds(Frame f, out FPVector3 min, out FPVector3 max)
        {
            var filtered = f.Filter<Chunk>();

            while (filtered.Next(out EntityRef _, out Chunk chunk))
            {
                if (chunk.Type != ChunkType.LobbyStart)
                {
                    continue;
                }

                FPVector3 halfExtent = new FPVector3(chunk.ChunkSizeWidth, FP._0, chunk.ChunkSizeDepth) * FP._0_50;
                FPVector3 center = f.Global->PlayerSpawnPosition;

                min = center - halfExtent;
                max = center + halfExtent;
                return true;
            }

            min = default;
            max = default;
            return false;
        }

        // Reads the Boss Arena chunk's own footprint center back out for
        // RunPhaseUtility.BeginBossEncounter (teleport destination + boss spawn position) - same
        // "the chunk IS the boundary, read back from its own Transform3D/ChunkSize" idiom
        // TryGetLobbyStartBounds above already uses, just a single center point rather than a
        // min/max pair since nothing needs the Boss Arena's full bounds today. Min-corner pivoted,
        // never rotated - Position IS the min corner, same math ComputeGridOrigin/
        // SpawnAtBossArenaDirectly already use elsewhere in this file.
        internal static bool TryFindBossArenaChunk(Frame f, out EntityRef chunkEntity)
        {
            var filtered = f.Filter<Chunk>();

            while (filtered.Next(out EntityRef entity, out Chunk chunk))
            {
                if (chunk.Type == ChunkType.Boss)
                {
                    chunkEntity = entity;
                    return true;
                }
            }

            chunkEntity = EntityRef.None;
            return false;
        }

        // Where connected players teleport to for the boss encounter (see
        // RunPhaseUtility.TeleportPlayersToBossArena) - the Boss chunk's own hand-authored
        // BossArena.TeleportPoints if baked (see BossArenaMarkerBaker; one per player slot, so
        // players land spread out instead of stacked on the same spot), otherwise a single point
        // at the chunk's plain geometric footprint center (the original, pre-marker behavior).
        // BossArena is its own component, not fields on Chunk itself - every chunk in the level
        // carries Chunk (dozens of them, from procedural generation), so these arrays would
        // otherwise sit wasted on every non-Boss chunk. Appends into positions rather than
        // returning a new list so callers reuse one buffer - same shape as
        // ResolveBossSpawnPositions below.
        internal static void ResolveBossTeleportPositions(Frame f, EntityRef chunkEntity, List<FPVector3> positions)
        {
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(chunkEntity);
            Transform3D* transform = f.Unsafe.GetPointer<Transform3D>(chunkEntity);

            if (f.Unsafe.TryGetPointer<BossArena>(chunkEntity, out var bossArena) == true && bossArena->TeleportPointCount > 0)
            {
                for (int i = 0; i < bossArena->TeleportPointCount; i++)
                {
                    positions.Add(transform->Position + transform->Rotation * bossArena->TeleportPoints[i]);
                }

                return;
            }

            positions.Add(FootprintCenter(chunk, transform));
        }

        // Where the boss(es) spawn (see RunPhaseUtility.SpawnBoss) - the Boss chunk's own
        // hand-authored BossArena.SpawnPoints if baked (SurvivalPhase.BossPrototype is spawned once
        // per point, so 2+ points spawn that many copies of the same boss, not different kinds),
        // otherwise a single spawn at the chunk's plain geometric footprint center (the original,
        // pre-marker behavior). Appends into positions rather than returning a new list so callers
        // (including EnemyFallSystem, which only wants the first entry for a fallen boss's respawn
        // point) can reuse one buffer.
        internal static void ResolveBossSpawnPositions(Frame f, EntityRef chunkEntity, List<FPVector3> positions)
        {
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(chunkEntity);
            Transform3D* transform = f.Unsafe.GetPointer<Transform3D>(chunkEntity);

            if (f.Unsafe.TryGetPointer<BossArena>(chunkEntity, out var bossArena) == true && bossArena->SpawnPointCount > 0)
            {
                for (int i = 0; i < bossArena->SpawnPointCount; i++)
                {
                    positions.Add(transform->Position + transform->Rotation * bossArena->SpawnPoints[i]);
                }

                return;
            }

            positions.Add(FootprintCenter(chunk, transform));
        }

        private static FPVector3 FootprintCenter(Chunk* chunk, Transform3D* transform)
        {
            return transform->Position + new FPVector3(chunk->ChunkSizeWidth, 0, chunk->ChunkSizeDepth) * FP._0_50;
        }

        // LobbyStart is pinned to the end, not the front - right after a hand-placed BossArena,
        // the only frontier cells that exist yet are Boss's own border, and a LobbyStart prototype
        // is typically authored to forbid Boss as a neighbor (Chunk.ForbiddenNeighbors, enforced in
        // TryPlaceRequest/ViolatesForbiddenNeighbor), so going first would leave it with zero legal
        // anchors and it'd always fail to place. Growing every other pool entry first diversifies the
        // frontier away from Boss, giving LobbyStart real non-forbidden anchors to land on by the time
        // its turn comes. Every other entry is shuffled so the resulting graph branches unpredictably.
        //
        // Rebuilt from scratch every generation tick off a private RNGSession seeded with
        // Global.LevelGenSeed (NOT f.RNG, which keeps advancing as chunks are placed) - a pure
        // function of (config, seed), so every tick and every client reconstructs the identical
        // ordered bag without any of it having to live in frame state. See StepGeneration.
        private List<ChunkRequest> BuildShuffledBag(LevelConfig config, ref RNGSession rng, bool logDetails)
        {
            List<ChunkRequest> startRequests = new List<ChunkRequest>();
            List<ChunkRequest> otherRequests = new List<ChunkRequest>();

            foreach (ChunkPoolEntry entry in config.ChunkPool)
            {
                if (entry.Prototypes == null || entry.Prototypes.Length == 0)
                {
                    if (logDetails)
                    {
                        Log.Warn($"[LevelGen] ChunkPool entry {entry.Type} (Count {entry.Count}) has no Prototypes assigned - skipping");
                    }

                    continue;
                }

                List<ChunkRequest> target = entry.Type == ChunkType.LobbyStart ? startRequests : otherRequests;

                for (int i = 0; i < entry.Count; i++)
                {
                    // Each instance independently rolls one of the entry's variants.
                    if (PickVariant(ref rng, entry, out AssetRef<EntityPrototype> prototype) == false)
                        continue;

                    target.Add(new ChunkRequest
                    {
                        Type = entry.Type,
                        Prototype = prototype,
                        MustHave = entry.MustHave,
                    });
                }
            }

            Shuffle(ref rng, otherRequests);
            otherRequests.AddRange(startRequests);
            return otherRequests;
        }

        // Deterministic weighted roll among an entry's variants - a single Next(0, totalWeight)
        // draw walked against each variant's own Weight, the same cumulative-weight shape
        // CombatDirectorUtility.TrySelectSpawn uses to pick an enemy group. A variant with Weight <= 0
        // is soft-disabled (skipped entirely), EXCEPT when every variant in the entry is <= 0: Unity
        // zero-inits a freshly added array element, so an unauthored/unmigrated list falls back to the
        // old uniform pick rather than silently placing nothing. Returns false only when the entry has
        // no usable variant at all.
        private bool PickVariant(ref RNGSession rng, ChunkPoolEntry entry, out AssetRef<EntityPrototype> prototype)
        {
            FP totalWeight = FP._0;

            for (int i = 0; i < entry.Prototypes.Length; i++)
            {
                if (entry.Prototypes[i].Weight > FP._0)
                    totalWeight += entry.Prototypes[i].Weight;
            }

            if (totalWeight <= FP._0)
            {
                // Next(0, N) is [0, N).
                prototype = entry.Prototypes[rng.Next(0, entry.Prototypes.Length)].Prototype;
                return true;
            }

            FP roll = rng.Next(FP._0, totalWeight);
            FP cumulative = FP._0;
            int chosenIndex = -1;

            for (int i = 0; i < entry.Prototypes.Length; i++)
            {
                if (entry.Prototypes[i].Weight <= FP._0)
                    continue;

                cumulative += entry.Prototypes[i].Weight;
                // Also latches the last positive-weight variant, so float rounding leaving `roll` a
                // hair under totalWeight still resolves to a real pick instead of none.
                chosenIndex = i;

                if (roll < cumulative)
                    break;
            }

            if (chosenIndex < 0)
            {
                prototype = default;
                return false;
            }

            prototype = entry.Prototypes[chosenIndex].Prototype;
            return true;
        }

        private void Shuffle<T>(ref RNGSession rng, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Placement only ever attaches new chunks to the existing frontier, so it has no way to
        // guarantee full coverage - it can leave scattered single-cell pockets fully enclosed by
        // chunks on every side. Those read identically to the open exterior beyond the level's own
        // edge in `occupied` (both are just `false`), so telling them apart means flood-filling
        // from the grid's outer border through every unoccupied cell first: anything the flood
        // reaches is open exterior (including the level's own edge, deliberately left untouched);
        // anything it can't reach is a genuine inner gap. No-ops if no GapFillerPrototype is
        // assigned, so a level with no filler configured behaves exactly as before this feature.
        private void FillInnerGaps(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied)
        {
            if (config.GapFillerPrototype.Id.IsValid == false)
            {
                Log.Debug("[LevelGen] no GapFillerPrototype assigned on LevelConfig - skipping inner gap fill");
                return;
            }

            int gridWidth = config.GridWidth;
            int gridDepth = config.GridDepth;
            bool[,] reachedFromOutside = new bool[gridWidth, gridDepth];
            Queue<(int X, int Z)> queue = new Queue<(int, int)>();

            for (int x = 0; x < gridWidth; x++)
            {
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x, 0);
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x, gridDepth - 1);
            }

            for (int z = 0; z < gridDepth; z++)
            {
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, 0, z);
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, gridWidth - 1, z);
            }

            while (queue.Count > 0)
            {
                (int x, int z) = queue.Dequeue();

                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x - 1, z);
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x + 1, z);
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x, z - 1);
                TrySeedOutsideCell(occupied, reachedFromOutside, queue, x, z + 1);
            }

            bool[,] needsFill = new bool[gridWidth, gridDepth];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int z = 0; z < gridDepth; z++)
                {
                    needsFill[x, z] = occupied[x, z] == false && reachedFromOutside[x, z] == false;
                }
            }

            MergeAndSpawnRuns(f, config, gridOriginX, gridOriginZ, needsFill, occupied);
        }

        // A fully enclosed pocket can be several cells wide AND deep - spawning one prototype
        // instance per cell would show it as a dense grid of small, individually-seamed blocks
        // instead of one clean piece. Each still-unclaimed needsFill cell grows a real 2D
        // rectangle: first as wide as it can go (RunLength along X), then tries to stack additional
        // full-width rows underneath (MaxRowDepth) as long as every cell across that whole width is
        // still fillable - so a genuinely square/rectangular pocket becomes ONE entity expanding on
        // both X and Z, not several 1-cell-thick strips. The resulting rectangle's PhysicsCollider3D
        // box is then stretched to cover it - same "resize the collider, the visual follows"
        // contract ColliderVisualScaleView already provides for SpawnEntitySkillAction/
        // SpawnRadiusUpgrade, just driven by grid cells instead of an authored scale. Greedy, not
        // globally optimal - an L-shaped pocket becomes 2+ rectangles, not one impossible shape.
        private void MergeAndSpawnRuns(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] needsFill, bool[,] occupied)
        {
            int gridWidth = needsFill.GetLength(0);
            int gridDepth = needsFill.GetLength(1);
            bool[,] claimed = new bool[gridWidth, gridDepth];
            int filledCellCount = 0;
            int runCount = 0;

            for (int x = 0; x < gridWidth; x++)
            {
                for (int z = 0; z < gridDepth; z++)
                {
                    if (needsFill[x, z] == false || claimed[x, z])
                    {
                        continue;
                    }

                    int width = RunLength(needsFill, claimed, x, z, 1, 0);
                    int depth = MaxRowDepth(needsFill, claimed, x, z, width);

                    ClaimRun(claimed, occupied, x, z, width, depth);
                    SpawnGapFillerRun(f, config, gridOriginX, gridOriginZ, x, z, width, depth);

                    filledCellCount += width * depth;
                    runCount++;
                }
            }

            Log.Debug($"[LevelGen] inner gap fill complete - filled {filledCellCount} cell(s) as {runCount} rectangle(s)");
        }

        // Counts consecutive needsFill-and-unclaimed cells starting at (x,z) stepping by (dx,dz).
        private int RunLength(bool[,] needsFill, bool[,] claimed, int x, int z, int dx, int dz)
        {
            int gridWidth = needsFill.GetLength(0);
            int gridDepth = needsFill.GetLength(1);
            int length = 0;

            while (x >= 0 && x < gridWidth && z >= 0 && z < gridDepth && needsFill[x, z] && claimed[x, z] == false)
            {
                length++;
                x += dx;
                z += dz;
            }

            return length;
        }

        // Given a row already known to be `width` cells wide and free, extends downward (+Z) one
        // full-width row at a time for as long as every cell in the next row is still
        // needsFill-and-unclaimed - this is what turns a 1-wide strip into a real rectangle.
        private int MaxRowDepth(bool[,] needsFill, bool[,] claimed, int originX, int originZ, int width)
        {
            int gridDepth = needsFill.GetLength(1);
            int depth = 1;

            while (originZ + depth < gridDepth && RowIsFree(needsFill, claimed, originX, originZ + depth, width))
            {
                depth++;
            }

            return depth;
        }

        private bool RowIsFree(bool[,] needsFill, bool[,] claimed, int originX, int z, int width)
        {
            for (int x = originX; x < originX + width; x++)
            {
                if (needsFill[x, z] == false || claimed[x, z])
                {
                    return false;
                }
            }

            return true;
        }

        private void ClaimRun(bool[,] claimed, bool[,] occupied, int originX, int originZ, int width, int depth)
        {
            for (int x = originX; x < originX + width; x++)
            {
                for (int z = originZ; z < originZ + depth; z++)
                {
                    claimed[x, z] = true;
                    occupied[x, z] = true;
                }
            }
        }

        // A merged run's entity is sized dynamically at spawn time, so unlike a real placed chunk
        // it has to be center-pivoted - FootprintCenterToWorld, not CellToWorld. Only X/Z extents
        // are touched; Y (wall height) is left exactly as authored on the prototype, so the artist
        // only ever has to get the height right once. Logs and leaves the entity at its authored
        // size if the prototype's collider isn't a Box, so a mismatched prototype is obvious rather
        // than silently spawning at the wrong size everywhere.
        private void SpawnGapFillerRun(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, int originX, int originZ, int width, int depth)
        {
            EntityRef entity = f.Create(config.GapFillerPrototype);
            PlacedChunk run = new PlacedChunk { OriginX = originX, OriginZ = originZ, Width = width, Depth = depth };
            f.Unsafe.GetPointer<Transform3D>(entity)->Position = FootprintCenterToWorld(config, gridOriginX, gridOriginZ, run);

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out PhysicsCollider3D* collider) == false || collider->Shape.Type != Shape3DType.Box)
            {
                Log.Error($"[LevelGen] GapFillerPrototype has no Box PhysicsCollider3D - entity {entity} spawned at its authored size instead of the {width}x{depth} cell run it's meant to cover");
                return;
            }

            FP halfWidth = (FP)width * config.CellSize * FP._0_50;
            FP halfDepth = (FP)depth * config.CellSize * FP._0_50;
            collider->Shape.Box.Extents = new FPVector3(halfWidth, collider->Shape.Box.Extents.Y, halfDepth);
        }

        // Shared by both the outer-border seeding loop and the BFS expansion below - marks a cell
        // reached-from-outside and enqueues it, unless it's out of bounds, already occupied by a
        // chunk, or already marked. Occupied cells act as walls the flood-fill can't pass through,
        // which is exactly what makes a fully-enclosed pocket unreachable from the border.
        private void TrySeedOutsideCell(bool[,] occupied, bool[,] reachedFromOutside, Queue<(int X, int Z)> queue, int x, int z)
        {
            int gridWidth = occupied.GetLength(0);
            int gridDepth = occupied.GetLength(1);

            if (x < 0 || z < 0 || x >= gridWidth || z >= gridDepth || occupied[x, z] || reachedFromOutside[x, z])
            {
                return;
            }

            reachedFromOutside[x, z] = true;
            queue.Enqueue((x, z));
        }

        // Creates the entity first so the footprint can be read straight off its own baked Chunk
        // component (LevelConfig never duplicates a size that's already authored on the prefab) -
        // then searches for a valid spot for that exact footprint, destroying the entity if none
        // exists anywhere on the grid. Safe because both happen inside the same tick: the entity
        // never exists in a frame the View layer would see, so a rejected candidate never costs a
        // prefab instantiation (which is what makes generation expensive in the first place).
        private bool TryPlaceRequest(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, ChunkRequest request, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed)
        {
            if (request.Prototype.Id.IsValid == false)
            {
                Log.Error($"[LevelGen] {request.Type} has no Prototype assigned in LevelConfig.ChunkPool - skipping");
                return false;
            }

            EntityRef entity = f.Create(request.Prototype);
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(entity);

            // Chunks are always placed unrotated - the authored Width/Depth map straight onto the
            // world grid's X/Z. (A random per-chunk rotation used to be rolled here, but the rotation
            // pipeline never worked correctly and was removed entirely.)
            int width = ToCellCount(chunk->ChunkSizeWidth, config);
            int depth = ToCellCount(chunk->ChunkSizeDepth, config);

            if (placed.Count == 0)
            {
                // Nothing placed yet and no pre-existing chunk in the scene - bootstrap at the grid
                // center. Nothing to attach to yet, so this skips the "must touch an already-placed
                // chunk" rule FindAllValidOrigins enforces for every request after this one.
                int centerX = (config.GridWidth - width) / 2;
                int centerZ = (config.GridDepth - depth) / 2;

                if (FitsInGrid(config, width, depth, centerX, centerZ) && IsFree(occupied, width, depth, centerX, centerZ))
                {
                    CommitPlacement(f, config, gridOriginX, gridOriginZ, entity, chunk, request.Type, width, depth, centerX, centerZ, occupied, neighborAllowedSides, placed);
                    return true;
                }
            }
            else
            {
                // Exhaustively scans every legal origin instead of gambling on a bounded number of
                // random anchor+offset guesses - a big footprint (e.g. a 20x10 Traversal chunk) has
                // very low odds of a uniformly-random offset landing on one of its few valid spots
                // once the grid has partially filled in, which used to make a MUST-HAVE chunk fail
                // and get destroyed even when plenty of room for it still existed elsewhere on the
                // grid. This is exhaustive rather than probabilistic, so a MUST-HAVE request now only
                // fails when there is genuinely nowhere left to put it.
                List<(int X, int Z)> candidates = FindAllValidOrigins(config, request.Type, chunk, width, depth, occupied, neighborAllowedSides, placed);

                if (candidates.Count > 0)
                {
                    (int originX, int originZ) = candidates[f.RNG->Next(0, candidates.Count)];
                    CommitPlacement(f, config, gridOriginX, gridOriginZ, entity, chunk, request.Type, width, depth, originX, originZ, occupied, neighborAllowedSides, placed);
                    return true;
                }
            }

            if (request.MustHave)
            {
                Log.Error($"[LevelGen] MUST-HAVE {request.Type} ({width}x{depth}) found no valid spot anywhere on the grid - destroying entity {entity}. Level will generate without a required chunk.");
            }
            else
            {
                Log.Debug($"[LevelGen] {request.Type} ({width}x{depth}) found no valid spot - destroying entity {entity}");
            }

            f.Destroy(entity);
            return false;
        }

        // Every legal origin for this footprint, in scan order (not shuffled - TryPlaceRequest
        // picks randomly among the results via f.RNG, so determinism only needs the CANDIDATE SET to
        // be identical across clients, not the scan order). A candidate must touch at least one
        // already-placed chunk (ComputeTouchedSides != 0) - not just fit in a free rectangle - so a
        // MUST-HAVE chunk never lands as a disconnected island unreachable from the rest of the level
        // graph; the touched side(s) must also be mutually allowed by both this chunk's own
        // AllowedConnectionSides and every neighbor's, exactly as CommitPlacement used to check
        // per-attempt.
        private List<(int X, int Z)> FindAllValidOrigins(LevelConfig config, ChunkType type, Chunk* chunk, int width, int depth, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed)
        {
            List<(int X, int Z)> candidates = new List<(int X, int Z)>();
            int maxOriginX = config.GridWidth - width;
            int maxOriginZ = config.GridDepth - depth;

            for (int originX = 0; originX <= maxOriginX; originX++)
            {
                for (int originZ = 0; originZ <= maxOriginZ; originZ++)
                {
                    if (IsValidGrowthPlacement(type, chunk, width, depth, originX, originZ, occupied, neighborAllowedSides, placed, config.MinConnectionWidthCells))
                    {
                        candidates.Add((originX, originZ));
                    }
                }
            }

            return candidates;
        }

        // originX/originZ are already guaranteed in-bounds by FindAllValidOrigins' loop range, so
        // unlike the old per-attempt CommitPlacement this has no FitsInGrid check to make.
        private bool IsValidGrowthPlacement(ChunkType type, Chunk* chunk, int width, int depth, int originX, int originZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed, int minConnectionWidthCells)
        {
            if (!IsFree(occupied, width, depth, originX, originZ))
            {
                return false;
            }

            byte touchedSides = ComputeTouchedSides(occupied, originX, originZ, width, depth);

            if (touchedSides == 0)
            {
                return false;
            }

            if (chunk->AllowedConnectionSides != default && (touchedSides & ~(byte)chunk->AllowedConnectionSides) != 0)
            {
                return false;
            }

            if (TouchesRestrictedNeighborSide(occupied, neighborAllowedSides, originX, originZ, width, depth))
            {
                return false;
            }

            if (HasUndersizedConnection(originX, originZ, width, depth, placed, minConnectionWidthCells))
            {
                return false;
            }

            // Data-driven per-prototype neighbor-type rule (see Chunk.ForbiddenNeighbors) - e.g. a
            // LobbyStart authored to forbid Boss/POI types so spawn never borders them. Applies to
            // every chunk, in both directions.
            if (ViolatesForbiddenNeighbor(type, chunk->ForbiddenNeighbors, placed, originX, originZ, width, depth))
            {
                return false;
            }

            return true;
        }

        private void CommitPlacement(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, EntityRef entity, Chunk* chunk, ChunkType type, int width, int depth, int originX, int originZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed)
        {
            // Chunks are min-corner pivoted and never rotated, so Transform3D.Position IS the
            // footprint's world min corner and the rotation is identity.
            FPVector3 worldMinCorner = CellToWorld(config, gridOriginX, gridOriginZ, originX, originZ);
            Transform3D* transform = f.Unsafe.GetPointer<Transform3D>(entity);
            transform->Position = worldMinCorner;
            transform->Rotation = FPQuaternion.Identity;
            chunk->OriginCellX = originX;
            chunk->OriginCellZ = originZ;

            PlacedChunk placedChunk = new PlacedChunk
            {
                Entity = entity,
                Type = type,
                OriginX = originX,
                OriginZ = originZ,
                Width = width,
                Depth = depth,
                AllowedConnectionSides = chunk->AllowedConnectionSides,
                ForbiddenNeighbors = chunk->ForbiddenNeighbors,
            };

            MarkOccupied(occupied, neighborAllowedSides, placedChunk);
            placed.Add(placedChunk);

            Log.Debug($"[LevelGen] placed {type} at ({originX},{originZ}) size {width}x{depth} -> entity {entity}, world position {transform->Position}");
        }

        private bool FitsInGrid(LevelConfig config, int width, int depth, int originX, int originZ)
        {
            return originX >= 0 && originZ >= 0
                && originX + width <= config.GridWidth
                && originZ + depth <= config.GridDepth;
        }

        private bool IsFree(bool[,] occupied, int width, int depth, int originX, int originZ)
        {
            for (int x = originX; x < originX + width; x++)
            {
                for (int z = originZ; z < originZ + depth; z++)
                {
                    if (occupied[x, z])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void MarkOccupied(bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, PlacedChunk chunk)
        {
            for (int x = chunk.OriginX; x < chunk.OriginX + chunk.Width; x++)
            {
                for (int z = chunk.OriginZ; z < chunk.OriginZ + chunk.Depth; z++)
                {
                    occupied[x, z] = true;
                    neighborAllowedSides[x, z] = chunk.AllowedConnectionSides;
                }
            }
        }

        // Symmetric counterpart to the self-check in CommitPlacement (which only rejects a
        // candidate touching a side outside ITS OWN AllowedConnectionSides) - this instead rejects
        // a placement that would touch an already-placed NEIGHBOR on a side that neighbor doesn't
        // allow anything to attach to. Without this, a dead-end chunk (e.g. Top only) could still
        // get new chunks attached on its other sides, since the self-check only ever gated the
        // incoming chunk, never the one already sitting there.
        private bool TouchesRestrictedNeighborSide(bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, int originX, int originZ, int width, int depth)
        {
            int gridWidth = occupied.GetLength(0);
            int gridDepth = occupied.GetLength(1);

            for (int x = originX; x < originX + width; x++)
            {
                if (NeighborRejectsConnection(occupied, neighborAllowedSides, gridWidth, gridDepth, x, originZ + depth, ChunkConnectionSide.Bottom))
                {
                    return true;
                }

                if (NeighborRejectsConnection(occupied, neighborAllowedSides, gridWidth, gridDepth, x, originZ - 1, ChunkConnectionSide.Top))
                {
                    return true;
                }
            }

            for (int z = originZ; z < originZ + depth; z++)
            {
                if (NeighborRejectsConnection(occupied, neighborAllowedSides, gridWidth, gridDepth, originX + width, z, ChunkConnectionSide.Left))
                {
                    return true;
                }

                if (NeighborRejectsConnection(occupied, neighborAllowedSides, gridWidth, gridDepth, originX - 1, z, ChunkConnectionSide.Right))
                {
                    return true;
                }
            }

            return false;
        }

        // requiredSideOnNeighbor is the side of the NEIGHBOR cell (x,z) the candidate footprint is
        // touching - e.g. a candidate touching a neighbor above it (its Top) is touching that
        // neighbor's Bottom. Out-of-bounds/unoccupied cells and unrestricted (default) neighbors
        // never reject, same "0 means unrestricted" convention AllowedConnectionSides already uses.
        private bool NeighborRejectsConnection(bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, int gridWidth, int gridDepth, int x, int z, ChunkConnectionSide requiredSideOnNeighbor)
        {
            if (x < 0 || z < 0 || x >= gridWidth || z >= gridDepth || occupied[x, z] == false)
            {
                return false;
            }
            

            ChunkConnectionSide neighborAllowed = neighborAllowedSides[x, z];

            if (neighborAllowed == default)
            {
                return false;
            }

            return ((byte)neighborAllowed & (byte)requiredSideOnNeighbor) == 0;
        }

        // Chunk.ChunkSizeWidth/ChunkSizeDepth are authored in the same raw units as CellSize, not
        // already a cell count - this converts one to the other.
        private int ToCellCount(int chunkSize, LevelConfig config)
        {
            return chunkSize / (int)config.CellSize;
        }

        // Chunk prefabs are min-corner pivoted (same convention as CubeVisualBuilder's cubes), not
        // center-pivoted - so a chunk's world position is just its origin cell scaled by CellSize,
        // always landing on an exact grid line regardless of its footprint size. originX/originZ
        // are grid-local (array) coordinates - gridOriginX/gridOriginZ (see ComputeGridOrigin)
        // shifts them back into world cell units before scaling.
        private FPVector3 CellToWorld(LevelConfig config, int gridOriginX, int gridOriginZ, int originX, int originZ)
        {
            FP worldX = (FP)(gridOriginX + originX) * config.CellSize;
            FP worldZ = (FP)(gridOriginZ + originZ) * config.CellSize;
            return new FPVector3(worldX, FP._0, worldZ);
        }

        // Unlike CellToWorld, the player spawn point should land in the middle of the LobbyStart
        // chunk's footprint (not its min-corner) so the player doesn't spawn touching a wall.
        // Stays at floor level (Y=0) - PlayerSpawnUtility.Spawn adds PlayerSpawnHeight on top of
        // this at the moment the player entity is actually created, not baked in here.
        private FPVector3 FootprintCenterToWorld(LevelConfig config, int gridOriginX, int gridOriginZ, PlacedChunk chunk)
        {
            FP worldX = ((FP)(gridOriginX + chunk.OriginX) + (FP)chunk.Width * FP._0_50) * config.CellSize;
            FP worldZ = ((FP)(gridOriginZ + chunk.OriginZ) + (FP)chunk.Depth * FP._0_50) * config.CellSize;
            return new FPVector3(worldX, FP._0, worldZ);
        }

        private PlacedChunk? FindChunkOfType(List<PlacedChunk> placed, ChunkType type)
        {
            foreach (PlacedChunk chunk in placed)
            {
                if (chunk.Type == type)
                {
                    return chunk;
                }
            }

            return null;
        }

        // Sanity check for the LobbyStart-can't-attach-to-Boss rule enforced during placement (see
        // TryPlaceRequest) - logs a clear pass/fail so it's obvious from the console whether the
        // rule actually held for this generated layout, rather than having to infer it from the
        // per-attempt rejection logs.
        private void VerifyStartNotAdjacentToBoss(List<PlacedChunk> placed)
        {
            PlacedChunk? startChunk = FindChunkOfType(placed, ChunkType.LobbyStart);
            PlacedChunk? bossChunk = FindChunkOfType(placed, ChunkType.Boss);

            if (startChunk == null || bossChunk == null)
            {
                return;
            }

            PlacedChunk start = startChunk.Value;
            PlacedChunk boss = bossChunk.Value;

            if (AreAdjacent(start, boss))
            {
                Log.Error($"[LevelGen] VERIFY FAILED - LobbyStart at ({start.OriginX},{start.OriginZ}) size {start.Width}x{start.Depth} ended up adjacent to Boss Arena at ({boss.OriginX},{boss.OriginZ}) size {boss.Width}x{boss.Depth}");
            }
            else
            {
                Log.Debug($"[LevelGen] VERIFY OK - LobbyStart at ({start.OriginX},{start.OriginZ}) is not adjacent to Boss Arena at ({boss.OriginX},{boss.OriginZ})");
            }
        }

        // True if the two footprints share a real edge (flush on one axis, with the perpendicular
        // ranges actually overlapping) - not just touching at a single corner or sitting on the
        // same boundary line without overlapping.
        private bool AreAdjacent(PlacedChunk a, PlacedChunk b)
        {
            bool xTouching = a.OriginX + a.Width == b.OriginX || b.OriginX + b.Width == a.OriginX;
            bool zOverlapping = a.OriginZ < b.OriginZ + b.Depth && b.OriginZ < a.OriginZ + a.Depth;

            if (xTouching && zOverlapping)
            {
                return true;
            }

            bool zTouching = a.OriginZ + a.Depth == b.OriginZ || b.OriginZ + b.Depth == a.OriginZ;
            bool xOverlapping = a.OriginX < b.OriginX + b.Width && b.OriginX < a.OriginX + a.Width;

            return zTouching && xOverlapping;
        }

        // Persists which other chunks each chunk directly borders (Chunk.ConnectedChunks) - read by
        // ChunkConnectivityUtility to gate Elite ("major" Director group) spawn placement, see
        // docs/survival-director.md's "Chunk Connectivity" section. Reuses the exact same AreAdjacent
        // rectangle test placement itself already validated every pair against, so two chunks found
        // adjacent here already passed AllowedConnectionSides/ForbiddenNeighbors during placement -
        // nothing about doors/openings needs re-checking. O(n^2) over `placed` (n = chunk count for
        // the level, small), done exactly once, here, since `placed` only exists as a local for the
        // final generation tick.
        private void ComputeChunkConnectivity(Frame f, List<PlacedChunk> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    if (AreAdjacent(placed[i], placed[j]) == false)
                    {
                        continue;
                    }

                    AddChunkConnection(f, placed[i].Entity, placed[j].Entity);
                    AddChunkConnection(f, placed[j].Entity, placed[i].Entity);
                }
            }
        }

        // One direction of a symmetric edge - ComputeChunkConnectivity always calls this in both
        // directions for a given pair. Logs rather than throws if the fixed array is ever exhausted,
        // since a missed connection only makes ChunkConnectivityUtility slightly more conservative
        // (an Elite spawn candidate near this chunk may be rejected when it shouldn't be), not a
        // correctness hazard for anything else.
        private void AddChunkConnection(Frame f, EntityRef from, EntityRef to)
        {
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(from);

            if (chunk->ConnectedChunkCount >= 8)
            {
                Log.Warn($"[LevelGen] chunk {from} already has 8 connected chunks recorded - {to} not added, ChunkConnectivityUtility may be overly conservative near it");
                return;
            }

            chunk->ConnectedChunks[chunk->ConnectedChunkCount] = to;
            chunk->ConnectedChunkCount++;
        }

        // True if the candidate footprint is flush against an already-placed chunk (shares a
        // straight edge) but the overlapping span along that edge is shorter than
        // minConnectionWidthCells - i.e. it would connect via a sliver narrower than intended
        // instead of a real, walkable passage. ComputeTouchedSides can't tell this on its own since
        // it only tracks a per-cell occupied bitmask, not which placed chunk owns each cell.
        private bool HasUndersizedConnection(int originX, int originZ, int width, int depth, List<PlacedChunk> placed, int minConnectionWidthCells)
        {
            foreach (PlacedChunk other in placed)
            {
                bool xFlush = originX + width == other.OriginX || other.OriginX + other.Width == originX;
                if (xFlush)
                {
                    int overlap = OverlapLength(originZ, depth, other.OriginZ, other.Depth);
                    if (overlap > 0 && overlap < minConnectionWidthCells)
                    {
                        return true;
                    }
                }

                bool zFlush = originZ + depth == other.OriginZ || other.OriginZ + other.Depth == originZ;
                if (zFlush)
                {
                    int overlap = OverlapLength(originX, width, other.OriginX, other.Width);
                    if (overlap > 0 && overlap < minConnectionWidthCells)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Length of the overlap between two 1D spans (0 if they don't overlap at all).
        private int OverlapLength(int aStart, int aLength, int bStart, int bLength)
        {
            int overlapStart = aStart > bStart ? aStart : bStart;
            int aEnd = aStart + aLength;
            int bEnd = bStart + bLength;
            int overlapEnd = aEnd < bEnd ? aEnd : bEnd;
            int overlap = overlapEnd - overlapStart;
            return overlap > 0 ? overlap : 0;
        }

        // A ChunkType as its single-bit ChunkTypeMask value. Relies on ChunkTypeMask's bit positions
        // being kept 1:1 with ChunkType's ordinals (enforced by the comment in Chunk.qtn), so the bit
        // for a type is simply 1 << its enum value.
        private static ChunkTypeMask ToMask(ChunkType type)
        {
            return (ChunkTypeMask)(1 << (int)type);
        }

        // True if placing a chunk of the given type/forbidden-set at this footprint would end up
        // adjacent to an already-placed chunk that either side refuses (see Chunk.ForbiddenNeighbors).
        // Bidirectional - rejects if the candidate forbids the neighbor's type OR the neighbor forbids
        // the candidate's type - so a rule authored on only one prototype of a pair still holds, and
        // it works regardless of placement order (every chunk is checked against all already-placed
        // ones, and chunks are never moved once placed). Builds a throwaway PlacedChunk for the
        // candidate footprint (Entity is irrelevant to AreAdjacent).
        private bool ViolatesForbiddenNeighbor(ChunkType type, ChunkTypeMask forbidden, List<PlacedChunk> placed, int originX, int originZ, int width, int depth)
        {
            byte candidateBit = (byte)ToMask(type);
            PlacedChunk candidate = new PlacedChunk { OriginX = originX, OriginZ = originZ, Width = width, Depth = depth };

            foreach (PlacedChunk other in placed)
            {
                if (AreAdjacent(candidate, other) == false)
                {
                    continue;
                }

                bool candidateForbidsOther = ((byte)forbidden & (byte)ToMask(other.Type)) != 0;
                bool otherForbidsCandidate = ((byte)other.ForbiddenNeighbors & candidateBit) != 0;

                if (candidateForbidsOther || otherForbidsCandidate)
                {
                    return true;
                }
            }

            return false;
        }

        // Which world-space side(s) of a candidate footprint actually border an already-occupied
        // cell - the level's own outer grid edge reads identically to empty space here (occupied
        // is just `false` past the border), so a footprint placed against the map edge is never
        // treated as "touching" that side.
        private byte ComputeTouchedSides(bool[,] occupied, int originX, int originZ, int width, int depth)
        {
            int gridWidth = occupied.GetLength(0);
            int gridDepth = occupied.GetLength(1);
            byte touchedSides = 0;

            for (int x = originX; x < originX + width; x++)
            {
                if (IsOccupiedAt(occupied, gridWidth, gridDepth, x, originZ + depth))
                {
                    touchedSides |= (byte)ChunkConnectionSide.Top;
                }

                if (IsOccupiedAt(occupied, gridWidth, gridDepth, x, originZ - 1))
                {
                    touchedSides |= (byte)ChunkConnectionSide.Bottom;
                }
            }

            for (int z = originZ; z < originZ + depth; z++)
            {
                if (IsOccupiedAt(occupied, gridWidth, gridDepth, originX + width, z))
                {
                    touchedSides |= (byte)ChunkConnectionSide.Right;
                }

                if (IsOccupiedAt(occupied, gridWidth, gridDepth, originX - 1, z))
                {
                    touchedSides |= (byte)ChunkConnectionSide.Left;
                }
            }

            return touchedSides;
        }

        private bool IsOccupiedAt(bool[,] occupied, int gridWidth, int gridDepth, int x, int z)
        {
            if (x < 0 || z < 0 || x >= gridWidth || z >= gridDepth)
            {
                return false;
            }

            return occupied[x, z];
        }

        // Only resolves PlayerSpawnPosition - actually spawning players is handled separately by
        // Update once PlayerSpawnUtility.IsReadyToSpawn (this same frame's chunk colliders need a
        // moment to settle in physics first), so nothing is spawned here even for players who
        // already joined.
        private void AssignPlayerSpawnPosition(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, List<PlacedChunk> placed)
        {
            PlacedChunk? spawnChunk = FindChunkOfType(placed, ChunkType.LobbyStart);

            if (spawnChunk == null)
            {
                Log.Error("[LevelGen] no LobbyStart chunk was placed - falling back to the Boss Arena. Check that LevelConfig.ChunkPool has a LobbyStart entry that could actually be placed.");
                spawnChunk = FindChunkOfType(placed, ChunkType.Boss);
            }

            if (spawnChunk == null)
            {
                Log.Error("[LevelGen] neither a LobbyStart chunk nor a Boss Arena was found - PlayerSpawnPosition left at default.");
                return;
            }

            FPVector3 spawnPosition = FootprintCenterToWorld(config, gridOriginX, gridOriginZ, spawnChunk.Value);
            f.Global->PlayerSpawnPosition = spawnPosition;
            Log.Debug($"[LevelGen] PlayerSpawnPosition set to {spawnPosition} ({spawnChunk.Value.Type} chunk at cell {spawnChunk.Value.OriginX},{spawnChunk.Value.OriginZ})");
        }

        // Called every frame once PlayerSpawnUtility.IsReadyToSpawn (not just once), so it also
        // catches anyone who joins later but still lands inside the same ready check as
        // PlayerInitSystem.OnPlayerAdded. The implicit int->PlayerRef conversion already treats
        // the int as a 0-based index and returns the 1-based PlayerRef for it (PlayerRef.op_Implicit:
        // "will return PlayerRef 1 for input 0") - so this loop must stay 0-based (0..PlayerCount-1),
        // not 1..PlayerCount, or it skips the real first player and checks one past the end.
        private void SpawnPendingPlayers(Frame f)
        {
            int spawnedCount = 0;

            for (int i = 0; i < f.MaxPlayerCount; i++)
            {
                PlayerRef player = i;
                RuntimePlayer runtimePlayerData = f.GetPlayerData(player);

                if (runtimePlayerData == null)
                {
                    continue;
                }

                if (PlayerSpawnUtility.HasSpawned(f, player))
                {
                    continue;
                }

                Log.Debug($"[LevelGen] spawning pending player {player} (PlayerCount={f.MaxPlayerCount}, avatar={runtimePlayerData.PlayerAvatar})");
                PlayerSpawnUtility.Spawn(f, player);
                spawnedCount++;
            }

            if (spawnedCount > 0)
            {
                Log.Debug($"[LevelGen] spawned {spawnedCount} player(s)");
            }
        }
    }
}
