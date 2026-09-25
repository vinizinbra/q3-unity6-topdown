using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Drop-in replacement for CubeVisualBuilder: sits on EACH level cube (pivot at its bottom min
// corner) and replaces that cube's visual with autotiled tileset pieces.
//
// Which cubes merge is decided by collision, not hierarchy: every TilesetPlatformBuilder in the same
// scene whose footprint (BoxCollider box, else mesh bounds) overlaps or touches this one at the same
// TOP height, with the same TilesetDefinition, joins one cluster - transitively, and across chunk
// prefabs, so the floors of two neighbouring chunks become one continuous platform. Only the cube's
// own MeshRenderer is hidden; children (baked shadows, detail slots, props) and colliders are never
// touched.
//
// The first cube that builds a cluster becomes its host and owns the generated tiles; every member
// remembers that host, so building again from any member (or a new cube joining later, e.g. a chunk
// spawned a few frames after its neighbour) first tears the old result down instead of doubling it.
//
// Runtime: OnStart cubes don't build immediately - they queue into TilesetBuildQueue, which flushes
// once no new cube has queued for a couple of frames, so a level streaming chunks in over several
// frames builds each merged floor once instead of once per chunk. OnEnable is for pooled objects;
// Manual is for callers that invoke Generate() themselves (TraversalPlatformView, SkipScaledParent).
// (The reset method isn't named Reset() on purpose: that's Unity's component-reset message.)
[DisallowMultipleComponent]
public class TilesetPlatformBuilder : MonoBehaviour
{
    public enum AutoGenerateMode
    {
        OnStart,
        OnEnable,
        Manual,
    }

    // What gets scattered on top of this cube's platforms (tileset's scatter lists).
    //   Auto    : Ground if the platform's bottom is below groundBelowY (a floor rising out of the pits -
    //             walkable), else Rooftop (a raised block players can't walk on).
    //   Ground  : small walk-through props only (rocks, tufts, cables).
    //   Rooftop : big props + edge props (containers, machines, fences).
    public enum SurfaceDecor
    {
        Auto,
        None,
        Ground,
        Rooftop,
    }

    [SerializeField, Required] private TilesetDefinition tileset;

    [SerializeField, Tooltip("OnStart = batched build after the level finished spawning (chunk cubes). OnEnable = every (re)activation, for pooled objects. Manual = only when Generate() is called.")]
    private AutoGenerateMode autoGenerate = AutoGenerateMode.OnStart;

    [SerializeField, Tooltip("Merge with touching same-height cubes (also across chunks). Turn off for moving/spawned cubes (e.g. Traversal platforms) so they're tiled alone and never pulled into a floor.")]
    private bool mergeWithNeighbours = true;

    [SerializeField, Tooltip("World size of one grid cell on X/Z. The grid is anchored at the host cube's min corner.")]
    private float cellSize = 1f;

    [SerializeField, Tooltip("Cubes whose tops differ by less than this are the same height; footprints closer than this count as touching.")]
    private float mergeTolerance = 0.05f;

    [SerializeField, Tooltip("Moves every generated tile on Y (keeps its height). Slightly negative so the tile tops sit just under the cube top and don't z-fight with anything authored exactly at it (ground decals, shadows, other surfaces). The host's value is used.")]
    private float verticalOffset = -0.02f;

    [SerializeField, Tooltip("Props scattered on top of the platform (see SurfaceDecor). The host's value is used.")]
    private SurfaceDecor surfaceDecor = SurfaceDecor.Auto;

    [SerializeField, Tooltip("Auto decor: platforms whose bottom is below this world Y count as walkable floors (Ground), others as raised blocks (Rooftop).")]
    private float groundBelowY = -0.5f;

    [SerializeField, Tooltip("Direction the gameplay camera looks along on XZ (it's fixed: rot 45,0,0 -> +Z). Wall props go mostly on walls facing the camera: full chance facing it, half on side walls, none on walls facing away (never seen).")]
    private Vector3 cameraViewDirection = Vector3.forward;

    [SerializeField, Tooltip("Embedded wall props never go below this world Y (keeps them above the water line on floors dropping into the pits).")]
    private float wallPropMinY = 0.05f;

    [SerializeField, Tooltip("Changes which variant each tile picks. Same seed + same layout = same result (the host's seed is used).")]
    private int variationSeed;

    [SerializeField, ReadOnly] private Transform generatedRoot;
    [SerializeField, HideInInspector] private TilesetPlatformBuilder host;
    [SerializeField, HideInInspector] private List<TilesetPlatformBuilder> members = new();
    [SerializeField, HideInInspector] private List<Renderer> hiddenRenderers = new();

    private const string LogTag = "Tileset";

    // The world's tileset (EnvironmentManager sets it from WorldTheme). When set it replaces every
    // cube's own `tileset`, so one chunk prefab renders as GrassCliff, NeonCity, ... per world; the
    // serialized field stays the fallback (no theme / scenes without an EnvironmentManager).
    public static TilesetDefinition TilesetOverride { get; private set; }

    public TilesetDefinition Tileset => TilesetOverride != null ? TilesetOverride : tileset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => TilesetOverride = null;

    // Changes the world tileset; rebuild = regenerate every already-built cluster in the loaded
    // scenes (edit-mode Apply Environment, or switching worlds mid-session). Cubes not built yet
    // simply pick the override up when they build.
    public static void SetTilesetOverride(TilesetDefinition value, bool rebuild)
    {
        if (TilesetOverride == value)
            return;

        TilesetOverride = value;
        if (rebuild)
            RebuildAll();
    }

    public static void RebuildAll()
    {
        for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            var candidates = FindCandidates(scene);
            var hosts = new List<TilesetPlatformBuilder>();
            foreach (var b in candidates)
            {
                if (b.members.Count > 0)
                    hosts.Add(b);
            }

            foreach (var b in hosts)
            {
                if (b.members.Count > 0) // may have been absorbed by an earlier rebuild in this loop
                    b.Generate(candidates);
            }
        }
    }

    // Cubes whose tiles this cube hosts (empty unless it's a host).
    public IReadOnlyList<TilesetPlatformBuilder> Members => members;

    private struct Cell
    {
        public float Bottom;
        public GameObject Template;
    }

    private void Start()
    {
        if (autoGenerate == AutoGenerateMode.OnStart && Application.isPlaying)
            TilesetBuildQueue.Enqueue(this);
    }

    private void OnEnable()
    {
        if (autoGenerate == AutoGenerateMode.OnEnable && Application.isPlaying)
            TilesetBuildQueue.Enqueue(this);
    }

    [Button("Generate Visual Tileset")]
    public void Generate()
    {
        Generate(FindCandidates(gameObject.scene));
    }

    // candidates = every builder that may merge (see FindCandidates); passed in so a batch flush
    // scans the scene once instead of once per cluster.
    public void Generate(List<TilesetPlatformBuilder> candidates)
    {
        var set = Tileset;
        if (set == null)
        {
            LogHelper.Error(LogTag, $"{name}: no TilesetDefinition assigned", this);
            return;
        }

        var cluster = FindCluster(candidates);

        // every cube's box, for the wall-prop ground test (a foot prop only spawns where some cube's top
        // surface is actually under it - not under bridges or over pits)
        groundBoxes.Clear();
        foreach (var c in candidates)
            groundBoxes.Add(WorldBox(c));
        foreach (var member in cluster)
        {
            if (member.host != null)
                member.host.ClearResult();
        }

        var origin = WorldBox(this).min;
        var cells = new Dictionary<Vector2Int, Cell>();
        var top = WorldBox(this).max.y;
        foreach (var member in cluster)
        {
            var b = WorldBox(member);
            var min = ToCell(b.min, origin, member);
            var max = ToCell(b.max, origin, member);
            if (max.x <= min.x || max.y <= min.y)
            {
                LogHelper.Warn(LogTag, $"{member.name} is smaller than one cell ({cellSize}) - skipped", member);
                continue;
            }

            for (var x = min.x; x < max.x; x++)
            for (var z = min.y; z < max.y; z++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var cell) || b.min.y < cell.Bottom)
                    cells[key] = new Cell { Bottom = b.min.y, Template = member.gameObject };
            }
        }

        generatedRoot = CreateGeneratedRoot();
        var platformCount = 0;
        var pieceCount = 0;
        var platformEdges = new List<(TilesetAutotiler.Placement placement, float bottom)>();
        foreach (var platform in SplitPlatforms(cells))
        {
            var platformRoot = new GameObject($"Platform_{platformCount++} ({platform.Count} cells)").transform;
            platformRoot.SetParent(generatedRoot, false);
            var template = cells[platform[0]].Template;

            // Solve the whole platform as ONE shape (no internal walls between merged cubes), but give
            // every tile its own bottom: the HIGHEST bottom of the cells it covers, so a tile never hangs
            // below the collider under it (e.g. a bridge merged with a deeper cube). Centers / edge runs
            // are then only merged between tiles that share a bottom.
            var unit = TilesetAutotiler.Solve(new HashSet<Vector2Int>(platform), set.TilingMode, set.UseInnerCorners, mergeCenters: false);
            var byBottom = new Dictionary<float, List<TilesetAutotiler.Placement>>();
            foreach (var placement in unit)
            {
                var b = PlacementBottom(placement, cells);
                if (!byBottom.TryGetValue(b, out var list))
                    byBottom[b] = list = new List<TilesetAutotiler.Placement>();
                list.Add(placement);
            }

            foreach (var (bottom, group) in byBottom)
            {
                var placements = group;
                if (set.TilingMode == TilesetAutotiler.Mode.DualGrid && set.EdgeRuns.Enabled)
                    placements = TilesetAutotiler.MergeEdges(placements, set.EdgeRuns);
                if (set.MergeCenters)
                    placements = TilesetAutotiler.MergeCenters(placements);

                foreach (var placement in placements)
                {
                    if (SpawnPiece(placement, origin, bottom, top, platformRoot, template))
                        pieceCount++;
                    if (placement.Key == TilesetAutotiler.EdgeKey || placement.Key == TilesetAutotiler.EdgeLongKey)
                        platformEdges.Add((placement, bottom));
                }
            }

            var platformBottom = float.MaxValue;
            foreach (var c in platform)
                platformBottom = Mathf.Min(platformBottom, cells[c].Bottom);
            ScatterSurface(set, platform, origin, top, platformBottom, platformRoot, template, platformEdges);
            platformEdges.Clear();
        }

        members.Clear();
        members.AddRange(cluster);
        foreach (var member in cluster)
        {
            member.host = this;
            member.HideOwnRenderer(hiddenRenderers);
        }

        LogHelper.Log(LogTag, $"{name}: built {platformCount} platform(s) from {cluster.Count} cube(s), {pieceCount} pieces", this);
    }

    [Button("Reset")]
    public void ResetVisualTileset()
    {
        if (host != null)
            host.ClearResult();
        else
            ClearResult();
    }

    // Tears down the result this cube hosts (tiles + hidden renderers) and unlinks its members.
    private void ClearResult()
    {
        foreach (var r in hiddenRenderers)
        {
            if (r == null)
                continue;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(r, "Restore tileset source renderer");
#endif
            r.enabled = true;
        }

        hiddenRenderers.Clear();

        foreach (var member in members)
        {
            if (member != null && member.host == this)
                member.host = null;
        }

        members.Clear();
        host = null;

        if (generatedRoot != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.DestroyObjectImmediate(generatedRoot.gameObject);
            else
#endif
                Destroy(generatedRoot.gameObject);
        }

        generatedRoot = null;
    }

    private void HideOwnRenderer(List<Renderer> hidden)
    {
        var r = GetComponent<MeshRenderer>();
        if (r == null || !r.enabled)
            return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RecordObject(r, "Hide tileset source renderer");
#endif
        r.enabled = false;
        hidden.Add(r);
    }

    // Every active builder in the scene (scene roots, not FindObjectsByType, so it also works in
    // Prefab Mode's preview scene).
    public static List<TilesetPlatformBuilder> FindCandidates(UnityEngine.SceneManagement.Scene scene)
    {
        var result = new List<TilesetPlatformBuilder>();
        if (!scene.IsValid())
            return result;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var b in root.GetComponentsInChildren<TilesetPlatformBuilder>())
        {
            if (b.isActiveAndEnabled)
                result.Add(b);
        }

        return result;
    }

    // BFS over overlapping/touching same-top, same-tileset cubes (this one included, first).
    private List<TilesetPlatformBuilder> FindCluster(List<TilesetPlatformBuilder> candidates)
    {
        var cluster = new List<TilesetPlatformBuilder> { this };
        if (!mergeWithNeighbours)
            return cluster;

        var visited = new HashSet<TilesetPlatformBuilder> { this };
        var boxes = new Dictionary<TilesetPlatformBuilder, Bounds>();
        foreach (var c in candidates)
            boxes[c] = WorldBox(c);
        boxes[this] = WorldBox(this);

        for (var i = 0; i < cluster.Count; i++)
        {
            var current = cluster[i];
            var box = boxes[current];
            box.Expand(new Vector3(mergeTolerance * 2f, 0f, mergeTolerance * 2f));

            foreach (var other in candidates)
            {
                if (visited.Contains(other) || other.Tileset != Tileset || !other.mergeWithNeighbours)
                    continue;

                var otherBox = boxes[other];
                if (Mathf.Abs(otherBox.max.y - box.max.y) > mergeTolerance || !IntersectsXZ(box, otherBox))
                    continue;

                visited.Add(other);
                cluster.Add(other);
            }
        }

        return cluster;
    }

    private static bool IntersectsXZ(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.z <= b.max.z && a.max.z >= b.min.z;
    }

    // World AABB of the cube's collision box (BoxCollider, even if disabled), else its mesh, else a
    // unit cube from the pivot - computed from the transform so it's valid in edit mode, on a
    // disabled collider/renderer, and before physics has synced.
    private static Bounds WorldBox(TilesetPlatformBuilder cube)
    {
        Vector3 localMin, localMax;
        var box = cube.GetComponent<BoxCollider>();
        var filter = cube.GetComponent<MeshFilter>();
        if (box != null)
        {
            localMin = box.center - box.size * 0.5f;
            localMax = box.center + box.size * 0.5f;
        }
        else if (filter != null && filter.sharedMesh != null)
        {
            localMin = filter.sharedMesh.bounds.min;
            localMax = filter.sharedMesh.bounds.max;
        }
        else
        {
            localMin = Vector3.zero;
            localMax = Vector3.one;
        }

        var m = cube.transform.localToWorldMatrix;
        var bounds = new Bounds(m.MultiplyPoint3x4(localMin), Vector3.zero);
        for (var i = 1; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? localMax.x : localMin.x,
                                     (i & 2) != 0 ? localMax.y : localMin.y,
                                     (i & 4) != 0 ? localMax.z : localMin.z);
            bounds.Encapsulate(m.MultiplyPoint3x4(corner));
        }

        return bounds;
    }

    private Vector2Int ToCell(Vector3 world, Vector3 origin, Object context)
    {
        var fx = (world.x - origin.x) / cellSize;
        var fz = (world.z - origin.z) / cellSize;
        var x = Mathf.RoundToInt(fx);
        var z = Mathf.RoundToInt(fz);
        if (Mathf.Abs(fx - x) > 0.05f || Mathf.Abs(fz - z) > 0.05f)
            LogHelper.Warn(LogTag, $"{context.name}: bound {world} is off the {cellSize} grid of {name} - snapped to cell ({x}, {z})", context);
        return new Vector2Int(x, z);
    }

    // Deterministic props on top of one platform. Interior cells only (all 8 neighbours solid), so
    // nothing sits on the lip / fade border; at most one prop per cell; a 2-cell prop also claims a
    // free interior neighbour. Everything is keyed by WORLD cell coordinates, so the same layout gives
    // the same props no matter which cube hosts the cluster.
    private void ScatterSurface(TilesetDefinition set, List<Vector2Int> platform, Vector3 origin, float top, float platformBottom,
        Transform parent, GameObject template, List<(TilesetAutotiler.Placement placement, float bottom)> edges)
    {
        var mode = surfaceDecor;
        if (mode == SurfaceDecor.Auto)
            mode = platformBottom < groundBelowY ? SurfaceDecor.Ground : SurfaceDecor.Rooftop;
        if (mode == SurfaceDecor.None)
            return;

        ScatterWalls(set, origin, top, parent, template, edges);

        var list = mode == SurfaceDecor.Ground ? set.GroundScatter : set.RooftopScatter;
        var density = mode == SurfaceDecor.Ground ? set.GroundDensity : set.RooftopDensity;
        var solid = new HashSet<Vector2Int>(platform);
        var ox = Mathf.RoundToInt(origin.x / cellSize);
        var oz = Mathf.RoundToInt(origin.z / cellSize);

        bool Interior(Vector2Int c)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!solid.Contains(new Vector2Int(c.x + dx, c.y + dz)))
                    return false;
            }

            return true;
        }

        int Hash(Vector2Int c, int salt)
        {
            unchecked
            {
                var h = (c.x + ox) * 73856093 ^ (c.y + oz) * 19349663 ^ salt * 83492791 ^ variationSeed * 2654435;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h;
            }
        }

        float U(int h) => (h & 0xffffff) / (float)0x1000000;

        if (list.Count > 0 && density > 0f)
        {
            var sorted = new List<Vector2Int>(platform);
            sorted.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var used = new HashSet<Vector2Int>();
            foreach (var c in sorted)
            {
                if (used.Contains(c) || !Interior(c) || U(Hash(c, 1)) >= density)
                    continue;
                if (!TilesetDefinition.PickScatter(list, Hash(c, 2), out var entry))
                    continue;

                var pos = new Vector2(c.x + 0.5f, c.y + 0.5f);
                var yaw = 90f * (Hash(c, 3) & 3);
                if (entry.Cells >= 2)
                {
                    var alongX = (Hash(c, 4) & 1) == 0;
                    var n = c + (alongX ? Vector2Int.right : Vector2Int.up);
                    if (used.Contains(n) || !Interior(n))
                        continue;
                    used.Add(n);
                    pos += alongX ? new Vector2(0.5f, 0f) : new Vector2(0f, 0.5f);
                    yaw = (alongX ? 0f : 90f) + 180f * (Hash(c, 5) & 1);
                }
                else if (entry.AnyYaw)
                {
                    pos += new Vector2(U(Hash(c, 6)) - 0.5f, U(Hash(c, 7)) - 0.5f) * 0.5f;
                    yaw = U(Hash(c, 8)) * 360f;
                }

                used.Add(c);
                var scale = entry.ScaleRange == Vector2.zero ? 1f : Mathf.Lerp(entry.ScaleRange.x, entry.ScaleRange.y, U(Hash(c, 9)));
                SpawnProp(entry.Model, new Vector3(origin.x + pos.x * cellSize, top + verticalOffset, origin.z + pos.y * cellSize), yaw, scale, parent, template);
            }
        }

        // Edge props (fences) along a raised block's rim: each Edge/Edge2 tile's own pivot + facing,
        // pushed inward (+Z in the edge's canonical frame, the solid side) so they stand on the top.
        if (mode == SurfaceDecor.Rooftop && set.RooftopEdgeProps.Count > 0 && set.RooftopEdgeChance > 0f)
        {
            foreach (var (e, _) in edges)
            {
                var cell = Vector2Int.RoundToInt(e.Position * 2f);   // unique key per placement (half cells)
                if (U(Hash(cell, 11)) >= set.RooftopEdgeChance)
                    continue;
                if (!TilesetDefinition.PickScatter(set.RooftopEdgeProps, Hash(cell, 12), out var entry))
                    continue;

                var rot = Quaternion.Euler(0f, 90f * e.Rotation, 0f);
                var inward = rot * new Vector3(0f, 0f, 0.3f * cellSize);
                var length = (e.Key == TilesetAutotiler.EdgeLongKey ? 2f : 1f) * (e.Stretch > 0f ? e.Stretch : 1f);
                var pos = new Vector3(origin.x + e.Position.x * cellSize, top + verticalOffset, origin.z + e.Position.y * cellSize) + inward;
                var go = SpawnProp(entry.Model, pos, 90f * e.Rotation, 1f, parent, template);
                if (go != null)
                    go.transform.localScale = new Vector3(go.transform.localScale.x * length * 0.9f, go.transform.localScale.y, go.transform.localScale.z);
            }
        }
    }

    // Wall props: one chance per Edge/Edge2 tile. Embedded props go into the face at a height in the
    // band below the lip (the upper ~2 units of the wall, where the camera actually sees it); the
    // pivot is pushed into the wall by the face's inset at that height (walls tuck in toward the
    // foot) plus a little, so the prop looks stuck in the dirt. Foot props stand at the tile's
    // bottom, only where that ground is visible (not on floors dropping into the pits).
    private void ScatterWalls(TilesetDefinition set, Vector3 origin, float top, Transform parent, GameObject template,
        List<(TilesetAutotiler.Placement placement, float bottom)> edges)
    {
        if (set.WallScatter.Count == 0 || set.WallDensity <= 0f)
            return;

        foreach (var (e, bottom) in edges)
        {
            // one chance per CELL of wall length (merged Edge2 / stretched pieces cover 2-3 cells), each in
            // its own slot along the piece, so density doesn't depend on how the walls got merged
            var length = (e.Key == TilesetAutotiler.EdgeLongKey ? 2f : 1f) * (e.Stretch > 0f ? e.Stretch : 1f);
            var slots = Mathf.Max(1, Mathf.RoundToInt(length));
            var key = Vector2Int.RoundToInt(e.Position * 2f);
            var outward = Quaternion.Euler(0f, 90f * e.Rotation, 0f) * Vector3.back;
            var view = new Vector3(cameraViewDirection.x, 0f, cameraViewDirection.z).normalized;
            var facing = -Vector3.Dot(outward, view);            // 1 = faces the camera, -1 = faces away
            var chance = set.WallDensity * (facing > 0.5f ? 1f : facing > -0.5f ? 0.5f : 0f);
            if (chance <= 0f)
                continue;

            for (var slot = 0; slot < slots; slot++)
            unchecked
            {
                var h0 = (key.x + Mathf.RoundToInt(origin.x / cellSize) * 2) * 73856093 ^ (key.y + Mathf.RoundToInt(origin.z / cellSize) * 2) * 19349663 ^ variationSeed * 2654435 ^ slot * 40503;
                float U(int salt) { var h = h0 ^ salt * 83492791; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffffff) / (float)0x1000000; }
                if (U(21) >= chance)
                    continue;
                if (!TilesetDefinition.PickScatter(set.WallScatter, (int)(U(22) * int.MaxValue), out var entry))
                    continue;

                var along = -length * 0.5f + (slot + 0.5f) * (length / slots) + (U(23) - 0.5f) * 0.3f;
                along = Mathf.Clamp(along, -length * 0.5f + 0.3f, length * 0.5f - 0.3f);
                var scale = entry.ScaleRange == Vector2.zero ? 1f : Mathf.Lerp(entry.ScaleRange.x, entry.ScaleRange.y, U(25));
                var propHeight = PropHeight(entry.Model) * scale;

                // keep-out band on the wall (e.g. a neon trim baked into the wall profile), in world Y
                var band = set.WallPropAvoidBand;
                var hasBand = band.y > band.x;
                var bandLo = bottom + (top - bottom) * band.x - set.WallPropAvoidMargin;
                var bandHi = bottom + (top - bottom) * band.y + set.WallPropAvoidMargin;

                float y, inset;
                if (entry.Foot)
                {
                    if (bottom < groundBelowY)
                        continue;
                    y = bottom;
                    inset = 0.1f;
                    if (hasBand && y + propHeight > bandLo)
                    {
                        // shrink to fit under the band; skip if that would make it too small to read
                        var fit = (bandLo - y) / Mathf.Max(propHeight, 1e-4f);
                        if (fit < 0.7f)
                            continue;
                        scale *= fit;
                        propHeight *= fit;
                    }
                }
                else
                {
                    var hi = top - 0.55f;
                    var lo = Mathf.Max(Mathf.Max(bottom + 0.1f, top - 2.2f), wallPropMinY);
                    if (hasBand)
                    {
                        // candidate ranges for the prop's BASE: fully below the band, or fully above it
                        var belowHi = Mathf.Min(hi, bandLo - propHeight);
                        var aboveLo = Mathf.Max(lo, bandHi);
                        var aboveHi = Mathf.Min(hi, top - 0.08f - propHeight);
                        var belowOk = belowHi >= lo;
                        var aboveOk = aboveHi >= aboveLo;
                        if (!belowOk && !aboveOk)
                            continue;
                        var useAbove = aboveOk && (!belowOk || U(27) < (aboveHi - aboveLo) / (aboveHi - aboveLo + belowHi - lo + 1e-4f));
                        y = useAbove ? Mathf.Lerp(aboveLo, aboveHi, U(24)) : Mathf.Lerp(lo, belowHi, U(24));
                    }
                    else
                    {
                        if (hi < lo)
                            continue;
                        y = Mathf.Lerp(lo, hi, U(24));
                    }
                    var f = Mathf.Clamp01((y - bottom) / Mathf.Max(top - bottom, 0.01f));
                    inset = 0.02f + 0.12f * (1f - f) + 0.015f;   // wall face inset at this height + a bit (embedded)
                }

                var rot = Quaternion.Euler(0f, 90f * e.Rotation, 0f);
                var pos = new Vector3(origin.x + e.Position.x * cellSize, y, origin.z + e.Position.y * cellSize)
                          + rot * new Vector3(along * cellSize, 0f, inset * cellSize);

                // foot props stand OUT in front of the wall (-Z in the edge frame): only spawn if there is
                // ground there at the wall's foot height (bridges, overhangs and pit edges have none) -
                // tested at both ends of the prop's footprint so it can't hang half over an edge
                if (entry.Foot)
                {
                    var front = rot * Vector3.back;
                    var side = rot * Vector3.right;
                    var probe = pos + front * (0.3f * cellSize);
                    if (!HasGroundAt(probe + side * 0.2f, y) || !HasGroundAt(probe - side * 0.2f, y))
                        continue;
                }

                SpawnProp(entry.Model, pos, 90f * e.Rotation + (U(26) - 0.5f) * 20f, scale, parent, template);
            }
        }
    }

    private readonly List<Bounds> groundBoxes = new();

    // Is there a walkable top surface at (x, z) at height y? Some cube whose XZ footprint contains the
    // point and whose top is within a few cm of y.
    private bool HasGroundAt(Vector3 p, float y)
    {
        foreach (var b in groundBoxes)
        {
            if (p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z && Mathf.Abs(b.max.y - y) < 0.12f)
                return true;
        }

        return false;
    }

    // Height of a prop model above its pivot (mesh bounds top x model scale), cached per model.
    private static readonly Dictionary<GameObject, float> propHeights = new();

    private static float PropHeight(GameObject model)
    {
        if (propHeights.TryGetValue(model, out var h))
            return h;

        h = 0f;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null)
                continue;
            var b = mf.sharedMesh.bounds;
            var m = mf.transform.localToWorldMatrix;   // prefab asset: root at origin
            for (var i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
                h = Mathf.Max(h, m.MultiplyPoint3x4(c).y);
            }
        }

        propHeights[model] = h;
        return h;
    }

        private GameObject SpawnProp(GameObject model, Vector3 position, float yaw, float scale, Transform parent, GameObject template)
    {
        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(model))
            go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        else
#endif
            go = Instantiate(model, parent);

        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation);
        go.transform.localScale = model.transform.localScale * scale;
        go.name = model.name;
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = template.layer;
        return go;
    }

    // Highest bottom among the solid cells a unit placement covers (the 1x1 square around its pivot:
    // the 4 quarter cells of a grid-vertex tile, or the one cell of a PerCell piece).
    private static float PlacementBottom(TilesetAutotiler.Placement placement, Dictionary<Vector2Int, Cell> cells)
    {
        var p = placement.Position;
        var bottom = float.MinValue;
        for (var x = Mathf.CeilToInt(p.x - 1.5f + 1e-3f); x <= Mathf.FloorToInt(p.x + 0.5f - 1e-3f); x++)
        for (var z = Mathf.CeilToInt(p.y - 1.5f + 1e-3f); z <= Mathf.FloorToInt(p.y + 0.5f - 1e-3f); z++)
        {
            if (cells.TryGetValue(new Vector2Int(x, z), out var cell))
                bottom = Mathf.Max(bottom, cell.Bottom);
        }

        // quantize so equal bottoms group together despite float noise
        return Mathf.Round(bottom * 1000f) / 1000f;
    }

    // 4-connected components of the cluster's cells.
    private static List<List<Vector2Int>> SplitPlatforms(Dictionary<Vector2Int, Cell> cells)
    {
        var result = new List<List<Vector2Int>>();
        var seen = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        var dirs = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        foreach (var start in cells.Keys)
        {
            if (!seen.Add(start))
                continue;

            var platform = new List<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                platform.Add(c);
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (cells.ContainsKey(n) && seen.Add(n))
                        queue.Enqueue(n);
                }
            }

            result.Add(platform);
        }

        return result;
    }

    private bool SpawnPiece(TilesetAutotiler.Placement placement, Vector3 origin, float bottom, float top, Transform parent, GameObject template)
    {
        var stretch = placement.Stretch > 0f ? placement.Stretch : 1f;
        var set = Tileset;
        var model = set.Get(placement.Key, VariantHash(placement, origin), stretch > 1.001f);
        if (model == null)
        {
            LogHelper.Warn(LogTag, $"{set.name} has no model for '{placement.Key}'", set);
            return false;
        }

        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(model))
            go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        else
#endif
            go = Instantiate(model, parent);

        var t = go.transform;
        t.SetPositionAndRotation(
            new Vector3(origin.x + placement.Position.x * cellSize, bottom + verticalOffset, origin.z + placement.Position.y * cellSize),
            Quaternion.Euler(0f, 90f * placement.Rotation, 0f) * model.transform.localRotation);
        t.localScale = Vector3.Scale(model.transform.localScale,
            new Vector3(cellSize * placement.Size.x * stretch, top - bottom, cellSize * placement.Size.y));
        go.name = placement.Size != Vector2Int.one
            ? $"{model.name} ({placement.Position.x}, {placement.Position.y}) {placement.Size.x}x{placement.Size.y}"
            : stretch > 1.001f
                ? $"{model.name} ({placement.Position.x}, {placement.Position.y}) r{placement.Rotation * 90} x{stretch:0.##}"
                : $"{model.name} ({placement.Position.x}, {placement.Position.y}) r{placement.Rotation * 90}";

        // Match the source cube's layer / static flags so batching, lighting and raycasts behave the same.
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = template.layer;
#if UNITY_EDITOR
        var flags = GameObjectUtility.GetStaticEditorFlags(template);
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
#endif
        return true;
    }

    // Integer hash of the tile's world grid coordinate + rotation + seed: spreads variants and stays
    // stable no matter which cube of the cluster ends up hosting it.
    private int VariantHash(TilesetAutotiler.Placement placement, Vector3 origin)
    {
        unchecked
        {
            var wx = placement.Seed.x + Mathf.RoundToInt(origin.x / cellSize);
            var wz = placement.Seed.y + Mathf.RoundToInt(origin.z / cellSize);
            var h = wx * 73856093 ^ wz * 19349663 ^ placement.Rotation * 83492791 ^ variationSeed * 2654435;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }

    // Tiles are placed in world space, so they must not sit under a scaled transform (cubes are
    // scaled to their size; WallChunk nests its cube under a 2x wrapper): parent to the nearest
    // unscaled ancestor so the tiles still move with the chunk / an animated pivot.
    private Transform CreateGeneratedRoot()
    {
        var root = new GameObject($"{name}_TilesetVisual").transform;
        var parent = transform.parent;
        while (parent != null && (parent.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
            parent = parent.parent;

        if (parent != null)
            root.SetParent(parent, false);
        else if (root.gameObject.scene != gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root.gameObject, gameObject.scene);

        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate visual tileset");
#endif
        return root;
    }
}
