using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Replaces the visuals of grid-aligned cubes (pivot at the bottom-left-back vertex, i.e. the
// cube's min corner) with tileset pieces. Put it on the parent of your cubes (or on one cube) and
// press Generate Visual Tileset:
//   1. every MeshRenderer under Source Root becomes a set of top-down cells ([min, max) on XZ);
//   2. cubes sharing the same bottom AND top height are grouped, and 4-connected cells in a group
//      merge into one platform;
//   3. TilesetAutotiler picks Center / Edge / Corner / 2x2 InnerCorner (+ fallbacks) per platform;
//   4. each piece is scaled on Y to the platform's height, the source renderers are hidden
//      (colliders stay untouched), and the tiles go under a generated container.
// Generating again is safe (it resets the previous result first); Reset restores the cube renderers.
// (The reset method isn't named Reset() on purpose: that's Unity's component-reset message.)
public class TilesetPlatformBuilder : MonoBehaviour
{
    [SerializeField, Required] private TilesetDefinition tileset;

    [SerializeField, Tooltip("Cubes to convert. Empty = this object and its children.")]
    private Transform sourceRoot;

    [SerializeField, Tooltip("Only MeshRenderers on these layers are treated as cubes.")]
    private LayerMask sourceLayers = ~0;

    [SerializeField, Tooltip("World size of one grid cell on X/Z.")]
    private float cellSize = 1f;

    [SerializeField, Tooltip("World position of a grid line crossing (cube min corners snap to this grid).")]
    private Vector3 gridOrigin = Vector3.zero;

    [SerializeField, Tooltip("Heights closer than this count as the same level when merging.")]
    private float heightTolerance = 0.01f;

    [SerializeField, Tooltip("Merge touching cubes of the same height into one platform. Off = every cube is tiled on its own.")]
    private bool mergeConnected = true;

    [SerializeField] private bool hideSourceRenderers = true;

    [SerializeField, Tooltip("Changes which variant each tile picks. Same seed + same layout = same result.")]
    private int variationSeed;

    [SerializeField, ReadOnly] private Transform generatedRoot;
    [SerializeField, HideInInspector] private List<Renderer> hiddenRenderers = new();

    private const string LogTag = "Tileset";

    private readonly struct LevelKey : System.IEquatable<LevelKey>
    {
        public readonly int Bottom;
        public readonly int Top;

        public LevelKey(int bottom, int top)
        {
            Bottom = bottom;
            Top = top;
        }

        public bool Equals(LevelKey other) => Bottom == other.Bottom && Top == other.Top;
        public override bool Equals(object obj) => obj is LevelKey other && Equals(other);
        public override int GetHashCode() => (Bottom * 397) ^ Top;
    }

    private class Level
    {
        public float Bottom;
        public float Top;
        public readonly Dictionary<Vector2Int, Renderer> Cells = new();
    }

    [Button("Generate Visual Tileset")]
    public void GenerateVisualTileset()
    {
        if (tileset == null)
        {
            LogHelper.Error(LogTag, $"{name}: no TilesetDefinition assigned", this);
            return;
        }

        ResetVisualTileset();

        var levels = CollectLevels();
        generatedRoot = CreateGeneratedRoot();

        var platformCount = 0;
        var pieceCount = 0;
        foreach (var level in levels.Values)
        {
            foreach (var platform in SplitPlatforms(level))
            {
                var platformRoot = new GameObject($"Platform_{platformCount++} (h{level.Top - level.Bottom:0.##}, {platform.Count} cells)").transform;
                platformRoot.SetParent(generatedRoot, false);

                var cells = new HashSet<Vector2Int>(platform);
                var template = level.Cells[platform[0]].gameObject;
                foreach (var placement in TilesetAutotiler.Solve(cells, tileset.TilingMode, tileset.UseInnerCorners, tileset.MergeCenters))
                {
                    if (SpawnPiece(placement, level, platformRoot, template))
                        pieceCount++;
                }
            }
        }

        if (hideSourceRenderers)
            HideSources(levels);

        LogHelper.Log(LogTag, $"{name}: built {platformCount} platform(s) from {levels.Count} height level(s), {pieceCount} pieces", this);
    }

    [Button("Reset")]
    public void ResetVisualTileset()
    {
        foreach (var r in hiddenRenderers)
        {
            if (r == null)
                continue;
#if UNITY_EDITOR
            Undo.RecordObject(r, "Restore tileset source renderer");
#endif
            r.enabled = true;
        }

        hiddenRenderers.Clear();

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

    private Dictionary<LevelKey, Level> CollectLevels()
    {
        var root = sourceRoot != null ? sourceRoot : transform;
        var levels = new Dictionary<LevelKey, Level>();
        var soloIndex = 0;

        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (generatedRoot != null && r.transform.IsChildOf(generatedRoot))
                continue;
            if ((sourceLayers.value & (1 << r.gameObject.layer)) == 0)
                continue;
            if (!TryWorldBounds(r, out var b) || b.size.y <= 0.0001f)
                continue;

            var min = Cell(b.min);
            var max = Cell(b.max);
            if (max.x <= min.x || max.y <= min.y)
            {
                LogHelper.Warn(LogTag, $"{r.name} is smaller than one cell ({cellSize}) - skipped", r);
                continue;
            }

            var key = new LevelKey(Mathf.RoundToInt(b.min.y / heightTolerance), Mathf.RoundToInt(b.max.y / heightTolerance));
            if (!mergeConnected)
                key = new LevelKey(key.Bottom, key.Top * 7919 + soloIndex++); // unique level per cube

            if (!levels.TryGetValue(key, out var level))
                levels[key] = level = new Level { Bottom = b.min.y, Top = b.max.y };

            for (var x = min.x; x < max.x; x++)
            for (var z = min.y; z < max.y; z++)
                level.Cells[new Vector2Int(x, z)] = r;
        }

        return levels;
    }

    // From the mesh, not Renderer.bounds: that one is unreliable once we've disabled the renderer.
    private static bool TryWorldBounds(Renderer r, out Bounds bounds)
    {
        bounds = default;
        var filter = r.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return false;

        var local = filter.sharedMesh.bounds;
        var m = r.transform.localToWorldMatrix;
        bounds = new Bounds(m.MultiplyPoint3x4(local.min), Vector3.zero);
        for (var i = 1; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? local.max.x : local.min.x,
                                     (i & 2) != 0 ? local.max.y : local.min.y,
                                     (i & 4) != 0 ? local.max.z : local.min.z);
            bounds.Encapsulate(m.MultiplyPoint3x4(corner));
        }

        return true;
    }

    private Vector2Int Cell(Vector3 world)
    {
        var fx = (world.x - gridOrigin.x) / cellSize;
        var fz = (world.z - gridOrigin.z) / cellSize;
        var x = Mathf.RoundToInt(fx);
        var z = Mathf.RoundToInt(fz);
        if (Mathf.Abs(fx - x) > 0.05f || Mathf.Abs(fz - z) > 0.05f)
            LogHelper.Warn(LogTag, $"{name}: bound {world} is off the {cellSize} grid - snapped to cell ({x}, {z})", this);
        return new Vector2Int(x, z);
    }

    // 4-connected components of one height level.
    private static List<List<Vector2Int>> SplitPlatforms(Level level)
    {
        var result = new List<List<Vector2Int>>();
        var seen = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        var dirs = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        foreach (var start in level.Cells.Keys)
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
                    if (level.Cells.ContainsKey(n) && seen.Add(n))
                        queue.Enqueue(n);
                }
            }

            result.Add(platform);
        }

        return result;
    }

    private bool SpawnPiece(TilesetAutotiler.Placement placement, Level level, Transform parent, GameObject template)
    {
        var model = tileset.Get(placement.Key, VariantHash(placement));
        if (model == null)
        {
            LogHelper.Warn(LogTag, $"{tileset.name} has no model for '{placement.Key}'", tileset);
            return false;
        }

        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(model))
            go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        else
#endif
            go = Instantiate(model, parent);

        var height = level.Top - level.Bottom;
        var t = go.transform;
        t.SetPositionAndRotation(
            new Vector3(gridOrigin.x + placement.Position.x * cellSize, level.Bottom, gridOrigin.z + placement.Position.y * cellSize),
            Quaternion.Euler(0f, 90f * placement.Rotation, 0f) * model.transform.localRotation);
        t.localScale = Vector3.Scale(model.transform.localScale,
            new Vector3(cellSize * placement.Size.x, height, cellSize * placement.Size.y));
        go.name = placement.Size == Vector2Int.one
            ? $"{model.name} ({placement.Position.x}, {placement.Position.y}) r{placement.Rotation * 90}"
            : $"{model.name} ({placement.Position.x}, {placement.Position.y}) {placement.Size.x}x{placement.Size.y}";

        // Match the source cube's layer / static flags so batching, lighting and raycasts behave the same.
        go.layer = template.layer;
#if UNITY_EDITOR
        var flags = GameObjectUtility.GetStaticEditorFlags(template);
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = template.layer;
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
        }
#endif
        return true;
    }

    // Integer hash of the tile coordinate + rotation + seed (spreads variants, stable across rebuilds).
    private int VariantHash(TilesetAutotiler.Placement placement)
    {
        unchecked
        {
            var h = placement.Seed.x * 73856093 ^ placement.Seed.y * 19349663 ^ placement.Rotation * 83492791 ^ variationSeed * 2654435;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }

    private Transform CreateGeneratedRoot()
    {
        var root = new GameObject($"{name}_TilesetVisual").transform;
        // Keep the tiles out from under a scaled/rotated cube so they don't inherit its transform.
        var parent = IsIdentityScaleAndRotation(transform) ? transform : transform.parent;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate visual tileset");
#endif
        return root;
    }

    private static bool IsIdentityScaleAndRotation(Transform t)
    {
        return (t.lossyScale - Vector3.one).sqrMagnitude < 1e-6f && Quaternion.Angle(t.rotation, Quaternion.identity) < 0.01f;
    }

    private void HideSources(Dictionary<LevelKey, Level> levels)
    {
        var unique = new HashSet<Renderer>();
        foreach (var level in levels.Values)
        foreach (var r in level.Cells.Values)
            unique.Add(r);

        foreach (var r in unique)
        {
            if (!r.enabled)
                continue;
#if UNITY_EDITOR
            Undo.RecordObject(r, "Hide tileset source renderer");
#endif
            r.enabled = false;
            hiddenRenderers.Add(r);
        }
    }
}
