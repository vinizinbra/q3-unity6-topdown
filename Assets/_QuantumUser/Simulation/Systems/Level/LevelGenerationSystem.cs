namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Generates the level once at Frame 0 (guarded by f.Global->LevelGenerated) out of
    // LevelConfig.ChunkPool. Any Chunk already in the world (e.g. a hand-placed BossArena with its
    // own pre-baked navmesh) seeds the grid; everything else is placed by f.Create around it, so
    // every client in the match generates the identical layout from the same f.RNG sequence.
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
                GenerateLevel(f);
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
                Log.Debug($"[LevelGen] ready to spawn players now - f.PlayerCount={f.PlayerCount}");
            }

            SpawnPendingPlayers(f);
        }

        private void GenerateLevel(Frame f)
        {
            if (f.RuntimeConfig.LevelConfig.Id.IsValid == false)
            {
                Log.Error("[LevelGen] no LevelConfig assigned on RuntimeConfig - skipping procedural generation, player will spawn directly in the Boss Arena instead of a LobbyStart chunk");
                SpawnAtBossArenaDirectly(f);
                f.Global->LevelGenerated = true;
                return;
            }

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);
            Log.Debug($"[LevelGen] starting - GridWidth={config.GridWidth}, GridDepth={config.GridDepth}, CellSize={config.CellSize}, ChunkPool entries={config.ChunkPool?.Length ?? 0}");

            (int gridOriginX, int gridOriginZ) = ComputeGridOrigin(f, config);
            Log.Debug($"[LevelGen] grid origin (world cell units) = ({gridOriginX},{gridOriginZ})");

            bool[,] occupied = new bool[config.GridWidth, config.GridDepth];
            ChunkConnectionSide[,] neighborAllowedSides = new ChunkConnectionSide[config.GridWidth, config.GridDepth];
            List<PlacedChunk> placed = new List<PlacedChunk>();

            SeedFromExistingChunks(f, config, gridOriginX, gridOriginZ, occupied, neighborAllowedSides, placed);
            Log.Debug($"[LevelGen] seeded from existing chunks - placed={placed.Count}");

            List<ChunkRequest> bag = BuildShuffledBag(f, config);
            Log.Debug($"[LevelGen] bag built - requests={bag.Count}");

            GrowLevel(f, config, gridOriginX, gridOriginZ, occupied, neighborAllowedSides, placed, bag);
            Log.Debug($"[LevelGen] grow complete - placed={placed.Count}");

            FillInnerGaps(f, config, gridOriginX, gridOriginZ, occupied);

            VerifyStartNotAdjacentToBoss(placed);

            AssignPlayerSpawnPosition(f, config, gridOriginX, gridOriginZ, placed);

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

        private void SeedFromExistingChunks(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed)
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
                };

                MarkOccupied(occupied, neighborAllowedSides, placedChunk);
                placed.Add(placedChunk);

                Log.Debug($"[LevelGen] found pre-existing {chunk.Type} chunk {entity} at grid cell ({placedChunk.OriginX},{placedChunk.OriginZ}) (world cell {worldOriginX},{worldOriginZ}), footprint {placedChunk.Width}x{placedChunk.Depth}");
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
        // the only frontier cells that exist yet are Boss's own border, and LobbyStart is never
        // allowed to attach directly to Boss (see TryPlaceRequest), so going first would leave it
        // with zero legal anchors every time and it'd always fail to place. Growing every other
        // pool entry first diversifies the frontier away from Boss, giving LobbyStart real
        // non-Boss anchors to land on by the time its turn comes. Every other entry is shuffled
        // via f.RNG so the resulting graph branches unpredictably.
        private List<ChunkRequest> BuildShuffledBag(Frame f, LevelConfig config)
        {
            List<ChunkRequest> startRequests = new List<ChunkRequest>();
            List<ChunkRequest> otherRequests = new List<ChunkRequest>();

            foreach (ChunkPoolEntry entry in config.ChunkPool)
            {
                List<ChunkRequest> target = entry.Type == ChunkType.LobbyStart ? startRequests : otherRequests;

                for (int i = 0; i < entry.Count; i++)
                {
                    target.Add(new ChunkRequest
                    {
                        Type = entry.Type,
                        Prototype = entry.Prototype,
                        MustHave = entry.MustHave,
                    });
                }
            }

            Shuffle(f, otherRequests);
            otherRequests.AddRange(startRequests);
            return otherRequests;
        }

        private void Shuffle<T>(Frame f, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = f.RNG->Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void GrowLevel(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed, List<ChunkRequest> bag)
        {
            foreach (ChunkRequest request in bag)
            {
                TryPlaceRequest(f, config, gridOriginX, gridOriginZ, request, occupied, neighborAllowedSides, placed);
            }
        }

        // GrowLevel only ever attaches new chunks to the existing frontier, so it has no way to
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
        // exists anywhere on the grid. Safe to create-then-destroy within the same one-shot frame-0
        // pass: the entity never exists in a frame the View layer would see.
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

        // Every legal origin for this footprint, in scan order (not shuffled - GrowLevel's caller
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
                    if (IsValidGrowthPlacement(type, chunk, width, depth, originX, originZ, occupied, neighborAllowedSides, placed))
                    {
                        candidates.Add((originX, originZ));
                    }
                }
            }

            return candidates;
        }

        // originX/originZ are already guaranteed in-bounds by FindAllValidOrigins' loop range, so
        // unlike the old per-attempt CommitPlacement this has no FitsInGrid check to make.
        private bool IsValidGrowthPlacement(ChunkType type, Chunk* chunk, int width, int depth, int originX, int originZ, bool[,] occupied, ChunkConnectionSide[,] neighborAllowedSides, List<PlacedChunk> placed)
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

            // LobbyStart is meant to be a safe area away from the boss, not a room right next to it.
            if (type == ChunkType.LobbyStart && WouldBeAdjacentToBoss(placed, originX, originZ, width, depth))
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

        // Used by TryPlaceRequest to reject a LobbyStart placement before it's committed - builds a
        // throwaway PlacedChunk for the candidate footprint (Entity/Type are irrelevant to
        // AreAdjacent) and checks it against every Boss chunk placed so far.
        private bool WouldBeAdjacentToBoss(List<PlacedChunk> placed, int originX, int originZ, int width, int depth)
        {
            PlacedChunk candidate = new PlacedChunk { OriginX = originX, OriginZ = originZ, Width = width, Depth = depth };

            foreach (PlacedChunk other in placed)
            {
                if (other.Type == ChunkType.Boss && AreAdjacent(candidate, other))
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

            for (int i = 0; i < f.PlayerCount; i++)
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

                Log.Debug($"[LevelGen] spawning pending player {player} (PlayerCount={f.PlayerCount}, avatar={runtimePlayerData.PlayerAvatar})");
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
