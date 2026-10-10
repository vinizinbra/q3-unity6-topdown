namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Level-wide walkability grid + A* for bots (see docs/bots.md "Navigation").
    //
    // One raycast straight down per 1x1 cell over every placed Chunk's footprint records the
    // top-most ground height (or "void" - water and pits are the same thing in this project, see
    // PlayerMovementProcessor). A step between two neighbouring cells is walkable when the rise is
    // within the player's climb (MovementDataAsset.MaxLedgeHeight) and the drop within a survivable
    // fall - so walls (their tops read as tall ground) and water both fall out of the same data.
    // Cells touching void are flagged Edge and cost more, so routes keep off shorelines.
    //
    // NOT simulation state: it's a pure function of the level's static geometry, built in ONE go
    // (never spread over ticks) the first time a bot asks after the level exists, and keyed by the
    // level's own seed + chunk layout. Every client - including a late joiner or a resimulation -
    // therefore sees the exact same grid at every tick it's used, so it can't desync and doesn't
    // need rolling back. It lives in a static cache only so it isn't rebuilt every tick. Geometry
    // that appears later (Traversal Challenge platforms) is deliberately not in it.
    public sealed unsafe class BotNavGrid
    {
        public static readonly FP CellSize = FP._1;

        // Walkability rules - see class comment. MaxClimb matches MovementDataAsset.MaxLedgeHeight
        // (auto-mantle); drops past AutoHopDrop trigger the player's auto-hop, which flings the bot
        // forward, so they're allowed but discouraged.
        private static readonly FP MaxClimb = FP._1 + FP._0_05;
        private static readonly FP MaxDrop = 4;
        private static readonly FP AutoHopDrop = FP._1;

        private static readonly FP RaycastTop = 40;
        private static readonly FP RaycastLength = 70;

        private const byte CellVoid = 0;
        private const byte CellWalkable = 1;
        private const byte CellEdge = 2;

        // A GroundNotJumpable obstacle (solid, never mantled - see PlayerMovementProcessor) sits on
        // top of this cell. Not walkable, but unlike Void it isn't a fall, so it doesn't turn its
        // neighbours into Edge cells.
        private const byte CellBlocked = 3;
        private const string NotJumpableLayerName = "GroundNotJumpable";

        // A* costs (integer, so the search is trivially deterministic).
        private const int StraightCost = 10;
        private const int DiagonalCost = 14;
        private const int EdgePenalty = 25;
        private const int ClimbPenalty = 4;
        private const int DropPenalty = 30;

        // Hard cap on cells one search may expand, so a hopeless query can't stall a tick.
        private const int MaxExpansions = 25000;

        // The cached grid for the current level, if any. See class comment for why a static is safe.
        private static BotNavGrid _cached;

        private readonly long _key;
        private readonly FP _originX;
        private readonly FP _originZ;
        private readonly int _width;
        private readonly int _depth;
        private readonly FP[] _height;
        private readonly byte[] _state;

        // A* scratch, reused between searches (stamped instead of cleared).
        private readonly int[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _openStamp;
        private readonly int[] _closedStamp;
        private int _stamp;
        private readonly List<(int f, int index)> _heap = new List<(int, int)>(1024);
        private readonly List<int> _cellPath = new List<int>(512);

        private static readonly int[] NeighborDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighborDz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // Returns the grid for this level, building it on first use. Null while the level isn't
        // generated yet or its colliders haven't settled (PlayerSpawnUtility waits the same way) -
        // callers fall back to plain steering then.
        public static BotNavGrid Get(Frame f)
        {
            if (f.Global->LevelGenerated == false || f.Global->TimeSinceLevelGenerated < FP._0_50)
                return null;

            long key = ComputeKey(f, out bool hasChunks);

            if (hasChunks == false)
                return null;

            if (_cached != null && _cached._key == key)
                return _cached;

            _cached = Build(f, key);
            return _cached;
        }

        private static long ComputeKey(Frame f, out bool hasChunks)
        {
            long key = f.Global->LevelGenSeed;
            hasChunks = false;

            var chunks = f.Filter<Chunk, Transform3D>();

            while (chunks.Next(out EntityRef _, out Chunk chunk, out Transform3D transform) == true)
            {
                hasChunks = true;
                key = key * 31 + transform.Position.X.RawValue;
                key = key * 31 + transform.Position.Z.RawValue;
                key = key * 31 + chunk.ChunkSizeWidth * 1000 + chunk.ChunkSizeDepth;
            }

            return key;
        }

        private BotNavGrid(long key, FP originX, FP originZ, int width, int depth)
        {
            _key = key;
            _originX = originX;
            _originZ = originZ;
            _width = width;
            _depth = depth;

            int count = width * depth;
            _height = new FP[count];
            _state = new byte[count];
            _gScore = new int[count];
            _cameFrom = new int[count];
            _openStamp = new int[count];
            _closedStamp = new int[count];
        }

        private static BotNavGrid Build(Frame f, long key)
        {
            FP minX = FP.UseableMax, minZ = FP.UseableMax, maxX = -FP.UseableMax, maxZ = -FP.UseableMax;
            var chunks = f.Filter<Chunk, Transform3D>();

            while (chunks.Next(out EntityRef _, out Chunk chunk, out Transform3D transform) == true)
            {
                minX = FPMath.Min(minX, transform.Position.X);
                minZ = FPMath.Min(minZ, transform.Position.Z);
                maxX = FPMath.Max(maxX, transform.Position.X + chunk.ChunkSizeWidth);
                maxZ = FPMath.Max(maxZ, transform.Position.Z + chunk.ChunkSizeDepth);
            }

            FP originX = FPMath.Floor(minX) - 2;
            FP originZ = FPMath.Floor(minZ) - 2;
            int width = FPMath.CeilToInt((maxX - originX) / CellSize) + 2;
            int depth = FPMath.CeilToInt((maxZ - originZ) / CellSize) + 2;

            var grid = new BotNavGrid(key, originX, originZ, width, depth);
            grid.Sample(f);
            grid.MarkEdges();

            Log.Debug($"[Bot] built nav grid {width}x{depth} at ({originX}, {originZ})");
            return grid;
        }

        private void Sample(Frame f)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FP quarter = CellSize * FP._0_25;

            LevelConfig levelConfig = f.FindAsset(f.RuntimeConfig.LevelConfig);
            FP minGroundY = levelConfig != null ? levelConfig.FallDeathHeight + FP._1 : -FP.UseableMax;

            // Only cells under (or a cell next to) a placed Chunk can have floor - everything else
            // is open water, so it's left void without spending raycasts on it.
            bool[] insideChunk = RasterizeChunks(f);
            int notJumpableMask = f.Layers.GetLayerMask(NotJumpableLayerName);

            for (int z = 0; z < _depth; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int index = x + z * _width;

                    if (insideChunk[index] == false)
                        continue;

                    FPVector3 center = CellCenter(x, z);

                    // Centre first; a miss re-tries four inner points so a sub-unit chunk seam
                    // running through the centre doesn't punch a hole in the floor.
                    if (TryGround(f, center, groundLayerMask, minGroundY, out FP height) == true
                        || TryGround(f, center + new FPVector3(quarter, FP._0, quarter), groundLayerMask, minGroundY, out height) == true
                        || TryGround(f, center + new FPVector3(-quarter, FP._0, quarter), groundLayerMask, minGroundY, out height) == true
                        || TryGround(f, center + new FPVector3(quarter, FP._0, -quarter), groundLayerMask, minGroundY, out height) == true
                        || TryGround(f, center + new FPVector3(-quarter, FP._0, -quarter), groundLayerMask, minGroundY, out height) == true)
                    {
                        _height[index] = height;
                        _state[index] = IsNotJumpableOnTop(f, center, height, notJumpableMask) == true ? CellBlocked : CellWalkable;
                    }
                }
            }
        }

        private static bool TryGround(Frame f, FPVector3 point, int layerMask, FP minGroundY, out FP height)
        {
            FPVector3 origin = new FPVector3(point.X, RaycastTop, point.Z);
            Hit3D? hit = f.Physics3D.Raycast(origin, FPVector3.Down, RaycastLength, layerMask, BotNavigation.BotQuery);

            height = hit.HasValue == true ? hit.Value.Point.Y : default;

            // Anything at the fall-death plane is the water/void, not floor.
            return hit.HasValue == true && height > minGroundY;
        }

        // The Ground-layer raycast passes straight through GroundNotJumpable colliders, so check that
        // layer separately: if its top is at or above the floor we found, the cell is blocked.
        private static bool IsNotJumpableOnTop(Frame f, FPVector3 point, FP groundHeight, int notJumpableMask)
        {
            if (notJumpableMask == 0)
                return false;

            FPVector3 origin = new FPVector3(point.X, RaycastTop, point.Z);
            Hit3D? hit = f.Physics3D.Raycast(origin, FPVector3.Down, RaycastLength, notJumpableMask, BotNavigation.BotQuery);

            return hit.HasValue == true && hit.Value.Point.Y >= groundHeight - FP._0_10;
        }

        private bool[] RasterizeChunks(Frame f)
        {
            var inside = new bool[_width * _depth];
            var chunks = f.Filter<Chunk, Transform3D>();

            while (chunks.Next(out EntityRef _, out Chunk chunk, out Transform3D transform) == true)
            {
                int x0 = System.Math.Max(0, FPMath.FloorToInt((transform.Position.X - _originX) / CellSize) - 1);
                int z0 = System.Math.Max(0, FPMath.FloorToInt((transform.Position.Z - _originZ) / CellSize) - 1);
                int x1 = System.Math.Min(_width - 1, FPMath.CeilToInt((transform.Position.X + chunk.ChunkSizeWidth - _originX) / CellSize) + 1);
                int z1 = System.Math.Min(_depth - 1, FPMath.CeilToInt((transform.Position.Z + chunk.ChunkSizeDepth - _originZ) / CellSize) + 1);

                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        inside[x + z * _width] = true;
                    }
                }
            }

            return inside;
        }

        // A walkable cell with any void (or unreachable-drop) neighbour is an Edge.
        private void MarkEdges()
        {
            for (int z = 0; z < _depth; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int index = x + z * _width;

                    if (_state[index] != CellWalkable)
                        continue;

                    for (int n = 0; n < 8; n++)
                    {
                        int nx = x + NeighborDx[n];
                        int nz = z + NeighborDz[n];

                        if (nx < 0 || nz < 0 || nx >= _width || nz >= _depth)
                        {
                            _state[index] = CellEdge;
                            break;
                        }

                        int neighbor = nx + nz * _width;

                        if (_state[neighbor] == CellVoid || _height[index] - _height[neighbor] > MaxDrop)
                        {
                            _state[index] = CellEdge;
                            break;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------ queries

        public FPVector3 CellCenter(int x, int z)
        {
            return new FPVector3(_originX + (x + FP._0_50) * CellSize, FP._0, _originZ + (z + FP._0_50) * CellSize);
        }

        private FPVector3 CellPoint(int index)
        {
            int x = index % _width;
            int z = index / _width;
            FPVector3 center = CellCenter(x, z);
            center.Y = _height[index];
            return center;
        }

        private bool TryGetCell(FPVector3 position, out int index)
        {
            int x = FPMath.FloorToInt((position.X - _originX) / CellSize);
            int z = FPMath.FloorToInt((position.Z - _originZ) / CellSize);
            index = x + z * _width;
            return x >= 0 && z >= 0 && x < _width && z < _depth;
        }

        private bool IsWalkable(int index) => _state[index] == CellWalkable || _state[index] == CellEdge;

        private bool CanStep(int from, int to)
        {
            if (IsWalkable(from) == false || IsWalkable(to) == false)
                return false;

            FP rise = _height[to] - _height[from];
            return rise <= MaxClimb && -rise <= MaxDrop;
        }

        // The cell under `position`, or the nearest walkable one within `radius` cells (a bot on a
        // seam, a target hovering over the shore). Ties resolve by scan order - deterministic.
        private bool TryResolveCell(FPVector3 position, int radius, out int index)
        {
            if (TryGetCell(position, out index) == true && IsWalkable(index) == true)
                return true;

            int cx = FPMath.FloorToInt((position.X - _originX) / CellSize);
            int cz = FPMath.FloorToInt((position.Z - _originZ) / CellSize);
            int best = -1;
            int bestDistance = int.MaxValue;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    int z = cz + dz;

                    if (x < 0 || z < 0 || x >= _width || z >= _depth)
                        continue;

                    int candidate = x + z * _width;

                    if (IsWalkable(candidate) == false)
                        continue;

                    int distance = dx * dx + dz * dz;

                    if (distance >= bestDistance)
                        continue;

                    best = candidate;
                    bestDistance = distance;
                }
            }

            index = best;
            return best >= 0;
        }

        // True when a straight walk from `from` to `to` stays on walkable cells with valid steps all
        // the way, without touching an Edge cell (other than where it starts or ends) - i.e. the bot
        // can just steer straight there.
        public bool IsStraightWalkable(FPVector3 from, FPVector3 to)
        {
            if (TryResolveCell(from, 1, out int startCell) == false || TryGetCell(to, out int endCell) == false)
                return false;

            FPVector3 delta = to - from;
            delta.Y = FP._0;
            FP length = delta.Magnitude;

            if (length < CellSize)
                return IsWalkable(endCell) == true;

            int steps = FPMath.CeilToInt(length / (CellSize * FP._0_50));
            FPVector3 stepVector = delta / steps;
            int previous = startCell;

            for (int i = 1; i <= steps; i++)
            {
                if (TryGetCell(from + stepVector * i, out int cell) == false)
                    return false;

                if (cell == previous)
                    continue;

                if (CanStep(previous, cell) == false)
                    return false;

                if (_state[cell] == CellEdge && cell != endCell)
                    return false;

                // Moving diagonally between cells: both shared orthogonal neighbours must hold, or
                // the straight line clips a void corner.
                int px = previous % _width, pz = previous / _width;
                int cx = cell % _width, cz = cell / _width;

                if (px != cx && pz != cz)
                {
                    if (IsWalkable(cx + pz * _width) == false || IsWalkable(px + cz * _width) == false)
                        return false;
                }

                previous = cell;
            }

            return true;
        }

        // A* from `from` to `to`, string-pulled into straight segments, written into `result` (up to
        // `capacity` waypoints, first leg first). Returns the count, or 0 if there's no route.
        public int FindPath(FPVector3 from, FPVector3 to, FPVector3* result, int capacity)
        {
            if (TryResolveCell(from, 2, out int start) == false || TryResolveCell(to, 3, out int goal) == false)
                return 0;

            if (start == goal)
            {
                result[0] = CellPoint(goal);
                return 1;
            }

            if (RunAStar(start, goal) == false)
                return 0;

            // _cellPath is goal -> start; string-pull from the start end.
            int count = 0;
            int anchor = _cellPath.Count - 1;

            while (anchor > 0 && count < capacity)
            {
                int next = anchor - 1;

                while (next > 0 && IsStraightWalkable(CellPoint(_cellPath[anchor]), CellPoint(_cellPath[next - 1])) == true)
                {
                    next--;
                }

                result[count] = CellPoint(_cellPath[next]);
                count++;
                anchor = next;
            }

            return count;
        }

        private bool RunAStar(int start, int goal)
        {
            _stamp++;
            _heap.Clear();
            _cellPath.Clear();

            int goalX = goal % _width;
            int goalZ = goal / _width;

            _gScore[start] = 0;
            _cameFrom[start] = -1;
            _openStamp[start] = _stamp;
            HeapPush(Heuristic(start, goalX, goalZ), start);

            int expansions = 0;

            while (_heap.Count > 0)
            {
                int current = HeapPop();

                if (_closedStamp[current] == _stamp)
                    continue;

                _closedStamp[current] = _stamp;

                if (current == goal)
                {
                    for (int walk = goal; walk != -1; walk = _cameFrom[walk])
                    {
                        _cellPath.Add(walk);
                    }

                    return true;
                }

                if (++expansions > MaxExpansions)
                    return false;

                int x = current % _width;
                int z = current / _width;

                for (int n = 0; n < 8; n++)
                {
                    int nx = x + NeighborDx[n];
                    int nz = z + NeighborDz[n];

                    if (nx < 0 || nz < 0 || nx >= _width || nz >= _depth)
                        continue;

                    int neighbor = nx + nz * _width;

                    if (_closedStamp[neighbor] == _stamp || CanStep(current, neighbor) == false)
                        continue;

                    bool diagonal = n >= 4;

                    // No corner-cutting past a void.
                    if (diagonal == true && (IsWalkable(nx + z * _width) == false || IsWalkable(x + nz * _width) == false))
                        continue;

                    int cost = diagonal == true ? DiagonalCost : StraightCost;

                    if (_state[neighbor] == CellEdge)
                        cost += EdgePenalty;

                    FP rise = _height[neighbor] - _height[current];

                    if (rise > FP._0_25)
                        cost += ClimbPenalty;
                    else if (-rise > AutoHopDrop)
                        cost += DropPenalty;

                    int tentative = _gScore[current] + cost;

                    if (_openStamp[neighbor] == _stamp && tentative >= _gScore[neighbor])
                        continue;

                    _openStamp[neighbor] = _stamp;
                    _gScore[neighbor] = tentative;
                    _cameFrom[neighbor] = current;
                    HeapPush(tentative + Heuristic(neighbor, goalX, goalZ), neighbor);
                }
            }

            return false;
        }

        // Octile distance - admissible for 8-connected moves at 10/14.
        private int Heuristic(int index, int goalX, int goalZ)
        {
            int dx = System.Math.Abs(index % _width - goalX);
            int dz = System.Math.Abs(index / _width - goalZ);
            return StraightCost * (dx + dz) + (DiagonalCost - 2 * StraightCost) * System.Math.Min(dx, dz);
        }

        // Binary min-heap on (f, index) - index breaks ties, keeping the search order deterministic.
        private void HeapPush(int f, int index)
        {
            _heap.Add((f, index));
            int i = _heap.Count - 1;

            while (i > 0)
            {
                int parent = (i - 1) / 2;

                if (Less(_heap[i], _heap[parent]) == false)
                    break;

                (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                i = parent;
            }
        }

        private int HeapPop()
        {
            int result = _heap[0].index;
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);

            int i = 0;

            while (true)
            {
                int left = i * 2 + 1;
                int right = left + 1;
                int smallest = i;

                if (left < _heap.Count && Less(_heap[left], _heap[smallest]) == true)
                    smallest = left;

                if (right < _heap.Count && Less(_heap[right], _heap[smallest]) == true)
                    smallest = right;

                if (smallest == i)
                    break;

                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }

            return result;
        }

        private static bool Less((int f, int index) a, (int f, int index) b)
        {
            return a.f < b.f || (a.f == b.f && a.index < b.index);
        }
    }
}
