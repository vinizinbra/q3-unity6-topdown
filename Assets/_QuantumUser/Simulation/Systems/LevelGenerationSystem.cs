namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
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

        // A candidate attachment point - an empty cell adjacent to an already-placed chunk.
        private struct FrontierCell
        {
            public int X;
            public int Z;
        }

        private const int MaxAttemptsPerRequest = 8;

        // MustHave entries get far more tries before being given up on - an optional chunk failing
        // to place is expected background noise (the frontier just didn't have room), but a
        // required one failing is a real problem worth burning extra attempts to avoid.
        private const int MaxAttemptsPerMustHaveRequest = 64;

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
                Log.Error("[LevelGen] no LevelConfig assigned on RuntimeConfig - skipping procedural generation, player will spawn directly in the Boss Arena instead of a Start chunk");
                SpawnAtBossArenaDirectly(f);
                f.Global->LevelGenerated = true;
                return;
            }

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);
            Log.Debug($"[LevelGen] starting - GridWidth={config.GridWidth}, GridDepth={config.GridDepth}, CellSize={config.CellSize}, ChunkPool entries={config.ChunkPool?.Length ?? 0}");

            (int gridOriginX, int gridOriginZ) = ComputeGridOrigin(f, config);
            Log.Debug($"[LevelGen] grid origin (world cell units) = ({gridOriginX},{gridOriginZ})");

            bool[,] occupied = new bool[config.GridWidth, config.GridDepth];
            List<PlacedChunk> placed = new List<PlacedChunk>();
            List<FrontierCell> frontier = new List<FrontierCell>();

            SeedFromExistingChunks(f, config, gridOriginX, gridOriginZ, occupied, placed, frontier);
            Log.Debug($"[LevelGen] seeded from existing chunks - placed={placed.Count}, frontier={frontier.Count}");

            List<ChunkRequest> bag = BuildShuffledBag(f, config);
            Log.Debug($"[LevelGen] bag built - requests={bag.Count}");

            GrowLevel(f, config, gridOriginX, gridOriginZ, occupied, placed, frontier, bag);
            Log.Debug($"[LevelGen] grow complete - placed={placed.Count}");

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

                FPVector3 position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                FPVector3 minCorner = position + MinCornerOffsetWorld(chunk.ChunkSizeWidth, chunk.ChunkSizeDepth, chunk.Rotation);
                int arenaWorldOriginX = FPMath.RoundToInt(minCorner.X / config.CellSize);
                int arenaWorldOriginZ = FPMath.RoundToInt(minCorner.Z / config.CellSize);
                int localWidth = ToCellCount(chunk.ChunkSizeWidth, config);
                int localDepth = ToCellCount(chunk.ChunkSizeDepth, config);
                int arenaWidth = EffectiveWidth(localWidth, localDepth, chunk.Rotation);
                int arenaDepth = EffectiveDepth(localWidth, localDepth, chunk.Rotation);

                int gridOriginX = arenaWorldOriginX - (config.GridWidth - arenaWidth) / 2;
                int gridOriginZ = arenaWorldOriginZ - (config.GridDepth - arenaDepth) / 2;

                return (gridOriginX, gridOriginZ);
            }

            // No Boss Arena found - fall back to a plain (0,0) origin.
            return (0, 0);
        }

        private void SeedFromExistingChunks(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied, List<PlacedChunk> placed, List<FrontierCell> frontier)
        {
            var filtered = f.Filter<Chunk>();
            while (filtered.Next(out EntityRef entity, out Chunk chunk))
            {
                FPVector3 position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                FPVector3 minCorner = position + MinCornerOffsetWorld(chunk.ChunkSizeWidth, chunk.ChunkSizeDepth, chunk.Rotation);
                int worldOriginX = FPMath.RoundToInt(minCorner.X / config.CellSize);
                int worldOriginZ = FPMath.RoundToInt(minCorner.Z / config.CellSize);
                int localWidth = ToCellCount(chunk.ChunkSizeWidth, config);
                int localDepth = ToCellCount(chunk.ChunkSizeDepth, config);

                PlacedChunk placedChunk = new PlacedChunk
                {
                    Entity = entity,
                    Type = chunk.Type,
                    OriginX = worldOriginX - gridOriginX,
                    OriginZ = worldOriginZ - gridOriginZ,
                    Width = EffectiveWidth(localWidth, localDepth, chunk.Rotation),
                    Depth = EffectiveDepth(localWidth, localDepth, chunk.Rotation),
                };

                MarkOccupied(occupied, placedChunk);
                placed.Add(placedChunk);
                AddFrontierCells(occupied, placedChunk, frontier);

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

                FPVector3 position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
                FPVector3 minCorner = position + MinCornerOffsetWorld(chunk.ChunkSizeWidth, chunk.ChunkSizeDepth, chunk.Rotation);
                FP effectiveWidth = SwapsAxes(chunk.Rotation) ? chunk.ChunkSizeDepth : chunk.ChunkSizeWidth;
                FP effectiveDepth = SwapsAxes(chunk.Rotation) ? chunk.ChunkSizeWidth : chunk.ChunkSizeDepth;
                FPVector3 spawnPosition = minCorner + new FPVector3(effectiveWidth, 0, effectiveDepth) * FP._0_50;

                f.Global->PlayerSpawnPosition = spawnPosition;
                Log.Debug($"[LevelGen] PlayerSpawnPosition set to {spawnPosition} (Boss Arena at {position}, no LevelConfig)");
                return;
            }

            Log.Error("[LevelGen] no LevelConfig and no Boss Arena chunk found - PlayerSpawnPosition left at default");
        }

        // Start is pinned to the end, not the front - right after a hand-placed BossArena, the
        // only frontier cells that exist yet are Boss's own border, and Start is never allowed to
        // attach directly to Boss (see TryPlaceRequest), so going first would leave it with zero
        // legal anchors every time and it'd always fail to place. Growing every other pool entry
        // first diversifies the frontier away from Boss, giving Start real non-Boss anchors to
        // land on by the time its turn comes. Every other entry is shuffled via f.RNG so the
        // resulting graph branches unpredictably.
        private List<ChunkRequest> BuildShuffledBag(Frame f, LevelConfig config)
        {
            List<ChunkRequest> startRequests = new List<ChunkRequest>();
            List<ChunkRequest> otherRequests = new List<ChunkRequest>();

            foreach (ChunkPoolEntry entry in config.ChunkPool)
            {
                List<ChunkRequest> target = entry.Type == ChunkType.Start ? startRequests : otherRequests;

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

        private void GrowLevel(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, bool[,] occupied, List<PlacedChunk> placed, List<FrontierCell> frontier, List<ChunkRequest> bag)
        {
            foreach (ChunkRequest request in bag)
            {
                TryPlaceRequest(f, config, gridOriginX, gridOriginZ, request, occupied, placed, frontier);
            }
        }

        // Creates the entity first so the footprint can be read straight off its own baked Chunk
        // component (LevelConfig never duplicates a size that's already authored on the prefab) -
        // then searches for a valid spot for that exact footprint, destroying the entity if none
        // of the attempts pan out. Safe to create-then-destroy within the same one-shot frame-0
        // pass: the entity never exists in a frame the View layer would see.
        private bool TryPlaceRequest(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, ChunkRequest request, bool[,] occupied, List<PlacedChunk> placed, List<FrontierCell> frontier)
        {
            if (request.Prototype.Id.IsValid == false)
            {
                Log.Error($"[LevelGen] {request.Type} has no Prototype assigned in LevelConfig.ChunkPool - skipping");
                return false;
            }

            EntityRef entity = f.Create(request.Prototype);
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(entity);
            int localWidth = ToCellCount(chunk->ChunkSizeWidth, config);
            int localDepth = ToCellCount(chunk->ChunkSizeDepth, config);

            // Chosen once per chunk, before the footprint is used for any grid math - a quarter
            // turn swaps which dimension acts as the grid width/depth (see EffectiveWidth/Depth).
            ChunkRotation rotation = (ChunkRotation)f.RNG->Next(0, 4);
            int width = EffectiveWidth(localWidth, localDepth, rotation);
            int depth = EffectiveDepth(localWidth, localDepth, rotation);

            if (frontier.Count == 0)
            {
                // Nothing placed yet and no pre-existing chunk in the scene - bootstrap at the grid center.
                int centerX = (config.GridWidth - width) / 2;
                int centerZ = (config.GridDepth - depth) / 2;

                if (CommitPlacement(f, config, gridOriginX, gridOriginZ, entity, chunk, request.Type, rotation, width, depth, centerX, centerZ, occupied, placed, frontier))
                {
                    return true;
                }
            }
            else
            {
                int maxAttempts = request.MustHave ? MaxAttemptsPerMustHaveRequest : MaxAttemptsPerRequest;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    if (!TryPickFrontierCell(f, occupied, frontier, out FrontierCell anchor))
                    {
                        break;
                    }

                    (int originX, int originZ) = RandomOffsetAroundAnchor(f, width, depth, anchor.X, anchor.Z);

                    // Start is meant to be a safe area away from the boss, not a room right next to
                    // it - reject this specific placement and try another anchor rather than
                    // aborting the whole request, since other frontier cells not bordering the Boss
                    // Arena are still perfectly valid for a Start chunk. Checked against the actual
                    // resulting footprint, not the anchor cell alone - RandomOffsetAroundAnchor only
                    // guarantees the anchor falls somewhere inside the footprint, so the footprint
                    // can still end up flush against the Boss Arena on a side the anchor cell itself
                    // never bordered.
                    if (request.Type == ChunkType.Start && WouldBeAdjacentToBoss(placed, originX, originZ, width, depth))
                    {
                        Log.Debug("[LevelGen] Start footprint would end up adjacent to the Boss Arena - trying a different anchor");
                        continue;
                    }

                    if (CommitPlacement(f, config, gridOriginX, gridOriginZ, entity, chunk, request.Type, rotation, width, depth, originX, originZ, occupied, placed, frontier))
                    {
                        return true;
                    }
                }
            }

            if (request.MustHave)
            {
                Log.Error($"[LevelGen] MUST-HAVE {request.Type} ({width}x{depth}) found no valid spot after {MaxAttemptsPerMustHaveRequest} attempts - destroying entity {entity}. Level will generate without a required chunk.");
            }
            else
            {
                Log.Debug($"[LevelGen] {request.Type} ({width}x{depth}) found no valid spot - destroying entity {entity}");
            }

            f.Destroy(entity);
            return false;
        }

        private bool TryPickFrontierCell(Frame f, bool[,] occupied, List<FrontierCell> frontier, out FrontierCell result)
        {
            while (frontier.Count > 0)
            {
                int index = f.RNG->Next(0, frontier.Count);
                FrontierCell candidate = frontier[index];

                if (occupied[candidate.X, candidate.Z])
                {
                    // Stale entry - this cell got occupied by a later placement since it was added.
                    frontier.RemoveAt(index);
                    continue;
                }

                result = candidate;
                return true;
            }

            result = default;
            return false;
        }

        // Picks a random footprint origin such that the anchor cell falls somewhere inside it -
        // lets a big chunk and a small chunk connect at any valid offset along their shared edge
        // instead of only lining up center-to-center.
        private (int, int) RandomOffsetAroundAnchor(Frame f, int width, int depth, int anchorX, int anchorZ)
        {
            int minOriginX = anchorX - width + 1;
            int minOriginZ = anchorZ - depth + 1;

            int originX = minOriginX + f.RNG->Next(0, width);
            int originZ = minOriginZ + f.RNG->Next(0, depth);

            return (originX, originZ);
        }

        private bool CommitPlacement(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, EntityRef entity, Chunk* chunk, ChunkType type, ChunkRotation rotation, int width, int depth, int originX, int originZ, bool[,] occupied, List<PlacedChunk> placed, List<FrontierCell> frontier)
        {
            if (!FitsInGrid(config, width, depth, originX, originZ))
            {
                Log.Debug($"[LevelGen] {type} at ({originX},{originZ}) size {width}x{depth} - out of grid bounds (grid {config.GridWidth}x{config.GridDepth})");
                return false;
            }

            if (!IsFree(occupied, width, depth, originX, originZ))
            {
                Log.Debug($"[LevelGen] {type} at ({originX},{originZ}) size {width}x{depth} - overlaps an occupied cell");
                return false;
            }

            FPVector3 worldMinCorner = CellToWorld(config, gridOriginX, gridOriginZ, originX, originZ);
            Transform3D* transform = f.Unsafe.GetPointer<Transform3D>(entity);
            transform->Position = RotatedChunkPosition(worldMinCorner, width, depth, config.CellSize, rotation);
            transform->Rotation = FPQuaternion.Euler(FP._0, RotationYaw(rotation), FP._0);
            chunk->OriginCellX = originX;
            chunk->OriginCellZ = originZ;
            chunk->Rotation = rotation;

            PlacedChunk placedChunk = new PlacedChunk
            {
                Entity = entity,
                Type = type,
                OriginX = originX,
                OriginZ = originZ,
                Width = width,
                Depth = depth,
            };

            MarkOccupied(occupied, placedChunk);
            placed.Add(placedChunk);
            AddFrontierCells(occupied, placedChunk, frontier);

            Log.Debug($"[LevelGen] placed {type} at ({originX},{originZ}) size {width}x{depth} rotation {rotation} -> entity {entity}, world position {transform->Position}");

            return true;
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

        private void MarkOccupied(bool[,] occupied, PlacedChunk chunk)
        {
            for (int x = chunk.OriginX; x < chunk.OriginX + chunk.Width; x++)
            {
                for (int z = chunk.OriginZ; z < chunk.OriginZ + chunk.Depth; z++)
                {
                    occupied[x, z] = true;
                }
            }
        }

        private void AddFrontierCells(bool[,] occupied, PlacedChunk chunk, List<FrontierCell> frontier)
        {
            int gridWidth = occupied.GetLength(0);
            int gridDepth = occupied.GetLength(1);

            for (int x = chunk.OriginX; x < chunk.OriginX + chunk.Width; x++)
            {
                TryAddFrontierCell(occupied, gridWidth, gridDepth, x, chunk.OriginZ - 1, frontier);
                TryAddFrontierCell(occupied, gridWidth, gridDepth, x, chunk.OriginZ + chunk.Depth, frontier);
            }

            for (int z = chunk.OriginZ; z < chunk.OriginZ + chunk.Depth; z++)
            {
                TryAddFrontierCell(occupied, gridWidth, gridDepth, chunk.OriginX - 1, z, frontier);
                TryAddFrontierCell(occupied, gridWidth, gridDepth, chunk.OriginX + chunk.Width, z, frontier);
            }
        }

        private void TryAddFrontierCell(bool[,] occupied, int gridWidth, int gridDepth, int x, int z, List<FrontierCell> frontier)
        {
            if (x < 0 || z < 0 || x >= gridWidth || z >= gridDepth || occupied[x, z])
            {
                return;
            }

            frontier.Add(new FrontierCell { X = x, Z = z });
        }

        // Chunk.ChunkSizeWidth/ChunkSizeDepth are authored in the same raw units as CellSize, not
        // already a cell count - this converts one to the other.
        private int ToCellCount(int chunkSize, LevelConfig config)
        {
            return chunkSize / (int)config.CellSize;
        }

        // Yaw in degrees for each rotation - matches CubeVisualBuilder's convention (0 deg = North
        // / +Z, Unity's +Y rotation cycles North->East->South->West, so each step is +90).
        private FP RotationYaw(ChunkRotation rotation)
        {
            return 0;
            switch (rotation)
            {
                case ChunkRotation.Degrees90:
                    return 90;
                case ChunkRotation.Degrees180:
                    return 180;
                case ChunkRotation.Degrees270:
                    return 270;
                default:
                    return 0;
            }
        }

        // A quarter turn swaps which local axis (Width along local X, Depth along local Z) ends up
        // along the world grid's X axis - a chunk authored 10 wide x 30 deep occupies a 30x10
        // footprint in world grid cells once rotated 90 or 270 degrees.
        private bool SwapsAxes(ChunkRotation rotation)
        {
            return rotation == ChunkRotation.Degrees90 || rotation == ChunkRotation.Degrees270;
        }

        private int EffectiveWidth(int localWidth, int localDepth, ChunkRotation rotation)
        {
            return SwapsAxes(rotation) ? localDepth : localWidth;
        }

        private int EffectiveDepth(int localWidth, int localDepth, ChunkRotation rotation)
        {
            return SwapsAxes(rotation) ? localWidth : localDepth;
        }

        // Chunks are min-corner pivoted (their own local origin = their own min corner, unrotated),
        // same convention CubeVisualBuilder.SpawnAt uses for individual pieces - so Transform3D.
        // Position is NOT the rotated footprint's actual min corner once a rotation is applied
        // (rotating in place around that local origin swings the footprint into a different world
        // region). This is the vector (in world units, not cells) from Position to where the real
        // min corner of the rotated bounding box ends up - used when READING an already-placed
        // chunk's footprint (e.g. a hand-placed BossArena) back out. rawWidth/rawDepth are
        // Chunk.ChunkSizeWidth/Depth (world units, unrotated).
        private FPVector3 MinCornerOffsetWorld(FP rawWidth, FP rawDepth, ChunkRotation rotation)
        {
            switch (rotation)
            {
                case ChunkRotation.Degrees90:
                    return new FPVector3(0, 0, -rawWidth);
                case ChunkRotation.Degrees180:
                    return new FPVector3(-rawWidth, 0, -rawDepth);
                case ChunkRotation.Degrees270:
                    return new FPVector3(-rawDepth, 0, 0);
                default:
                    return FPVector3.Zero;
            }
        }

        // Inverse of MinCornerOffsetWorld - used when PLACING a new chunk. Works from the
        // footprint's center rather than its min corner (the center doesn't move under rotation),
        // the same technique CubeVisualBuilder.SpawnAt uses: place the center where it needs to be,
        // then rotate the fixed local-space vector from center to the prefab's own local origin to
        // find where Transform3D.Position actually needs to sit for the rotated footprint to land
        // exactly on the target cells. effectiveWidth/Depth are already rotation-swapped (the grid
        // footprint being targeted); localWidth/Depth are un-swapped back out from them.
        private FPVector3 RotatedChunkPosition(FPVector3 worldMinCorner, int effectiveWidth, int effectiveDepth, FP cellSize, ChunkRotation rotation)
        {
            int localWidth = SwapsAxes(rotation) ? effectiveDepth : effectiveWidth;
            int localDepth = SwapsAxes(rotation) ? effectiveWidth : effectiveDepth;

            FPVector3 footprintCenter = worldMinCorner + new FPVector3(effectiveWidth, 0, effectiveDepth) * FP._0_50 * cellSize;
            FPVector3 centerToLocalOrigin = new FPVector3(-localWidth, 0, -localDepth) * FP._0_50 * cellSize;
            FPQuaternion rotationQuaternion = FPQuaternion.Euler(FP._0, RotationYaw(rotation), FP._0);

            return footprintCenter + rotationQuaternion * centerToLocalOrigin;
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

        // Unlike CellToWorld, the player spawn point should land in the middle of the Start
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

        // Sanity check for the Start-can't-attach-to-Boss rule enforced during placement (see
        // TryPlaceRequest) - logs a clear pass/fail so it's obvious from the console whether the
        // rule actually held for this generated layout, rather than having to infer it from the
        // per-attempt rejection logs.
        private void VerifyStartNotAdjacentToBoss(List<PlacedChunk> placed)
        {
            PlacedChunk? startChunk = FindChunkOfType(placed, ChunkType.Start);
            PlacedChunk? bossChunk = FindChunkOfType(placed, ChunkType.Boss);

            if (startChunk == null || bossChunk == null)
            {
                return;
            }

            PlacedChunk start = startChunk.Value;
            PlacedChunk boss = bossChunk.Value;

            if (AreAdjacent(start, boss))
            {
                Log.Error($"[LevelGen] VERIFY FAILED - Start at ({start.OriginX},{start.OriginZ}) size {start.Width}x{start.Depth} ended up adjacent to Boss Arena at ({boss.OriginX},{boss.OriginZ}) size {boss.Width}x{boss.Depth}");
            }
            else
            {
                Log.Debug($"[LevelGen] VERIFY OK - Start at ({start.OriginX},{start.OriginZ}) is not adjacent to Boss Arena at ({boss.OriginX},{boss.OriginZ})");
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

        // Used by TryPlaceRequest to reject a Start placement before it's committed - builds a
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

        // Only resolves PlayerSpawnPosition - actually spawning players is handled separately by
        // Update once PlayerSpawnUtility.IsReadyToSpawn (this same frame's chunk colliders need a
        // moment to settle in physics first), so nothing is spawned here even for players who
        // already joined.
        private void AssignPlayerSpawnPosition(Frame f, LevelConfig config, int gridOriginX, int gridOriginZ, List<PlacedChunk> placed)
        {
            PlacedChunk? spawnChunk = FindChunkOfType(placed, ChunkType.Start);

            if (spawnChunk == null)
            {
                Log.Error("[LevelGen] no Start chunk was placed - falling back to the Boss Arena. Check that LevelConfig.ChunkPool has a Start entry that could actually be placed.");
                spawnChunk = FindChunkOfType(placed, ChunkType.Boss);
            }

            if (spawnChunk == null)
            {
                Log.Error("[LevelGen] neither a Start chunk nor a Boss Arena was found - PlayerSpawnPosition left at default.");
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
