using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

[System.Serializable]
public class PlatformConfig
{
    [Tooltip("How many height levels this platform rises above the surface it sits on.")]
    public int heightStep = 1;
    public int radius = 4;
    [Range(0f, 1f)] public float irregularity = 0.35f;
    public float noiseScale = 0.2f;
    public int seed;

    [Tooltip("Platforms generated on top of this one, nested inside its footprint.")]
    public List<PlatformConfig> children = new List<PlatformConfig>();
}

[System.Serializable]
public class IslandConfig
{
    public string name = "Island";

    [Tooltip("Grid-cell offset of this island's center, shared across the whole level.")]
    public Vector2Int center;

    public int radius = 12;
    [Range(0f, 1f)] public float irregularity = 0.35f;
    public float noiseScale = 0.15f;
    public int seed;

    public List<PlatformConfig> platforms = new List<PlatformConfig>();
}

public class IslandBuilder : MonoBehaviour
{
    private static readonly Vector2Int North = new Vector2Int(0, 1);
    private static readonly Vector2Int South = new Vector2Int(0, -1);
    private static readonly Vector2Int East = new Vector2Int(1, 0);
    private static readonly Vector2Int West = new Vector2Int(-1, 0);

    private static readonly Vector2Int[] CardinalDirections = { North, South, East, West };

    // An empty cell gets a groundEdge only where two adjacent (perpendicular) neighbors are
    // both filled - that's the concave "notch" next to a bump, e.g. grid row "0 1 0 0" over a
    // filled row "1 1 1 1" only marks the two cells touching the bump: "e 1 e 0".
    // The prefab's default (0 deg) fill faces the (South, East) pocket. Unity's +Y rotation
    // cycles North->East->South->West, so walking (South,East) forward by +90 steps
    // North->East->South->West runs the pair list backwards - hence the -90 (270/180/90) steps below.
    private static readonly (Vector2Int a, Vector2Int b, float angle)[] CornerPairs =
    {
        (South, East, 0f),
        (East, North, 270f),
        (North, West, 180f),
        (West, South, 90f),
    };

    [SerializeField] private GameObject groundPrefab;
    [SerializeField] private GameObject groundEdgePrefab;
    [SerializeField] private float cellSize = 2f;

    [Tooltip("World-space height of one level. Assumes ground/edge prefabs are authored 1 unit tall.")]
    [SerializeField] private float cellHeight = 1f;

    [SerializeField] private float edgeYawOffset;

    [SerializeField] private List<IslandConfig> islands = new List<IslandConfig>();

    [Tooltip("Width in cells of the land bridge carved between consecutive islands in the list above.")]
    [SerializeField] private int bridgeWidth = 2;

    [Tooltip("Noise layers used to shape coastlines. Lower = broader, flatter, straighter edges (fewer, bigger bumps). Higher = jagged, detailed edges.")]
    [SerializeField, Range(1, 5)] private int noiseOctaves = 2;

    [Tooltip("How much each extra noise octave contributes. Lower = the fine-detail octaves barely matter, edges stay clean.")]
    [SerializeField, Range(0f, 1f)] private float noisePersistence = 0.35f;

    [SerializeField, HideInInspector] private Transform generatedRoot;

    [Button]
    public void GenerateGenericIsland()
    {
        islands = new List<IslandConfig>
        {
            new IslandConfig
            {
                name = "Island",
                center = Vector2Int.zero,
                radius = 12,
                irregularity = 0.4f,
                noiseScale = 0.15f,
                seed = 0,
            },
        };

        Generate();
    }

    [Button]
    public void Generate()
    {
        Clear();

        var heights = new Dictionary<Vector2Int, int>();
        var islandFootprints = new List<HashSet<Vector2Int>>();

        foreach (IslandConfig island in islands)
        {
            HashSet<Vector2Int> cells = GenerateBlob(island.center, island.radius, island.irregularity, island.noiseScale, island.seed);
            foreach (Vector2Int cell in cells)
            {
                RaiseHeight(heights, cell, 1);
            }

            islandFootprints.Add(cells);

            var placementRng = new System.Random(island.seed);
            foreach (PlatformConfig platform in island.platforms)
            {
                GeneratePlatform(platform, cells, 1, heights, placementRng);
            }
        }

        for (int i = 0; i < islandFootprints.Count - 1; i++)
        {
            ConnectIslands(islandFootprints[i], islandFootprints[i + 1], heights);
        }

        generatedRoot = new GameObject("Generated").transform;
        generatedRoot.SetParent(transform, false);

        foreach (KeyValuePair<Vector2Int, int> entry in heights)
        {
            SpawnGround(entry.Key, entry.Value);
        }

        int maxHeight = 0;
        foreach (int height in heights.Values)
        {
            maxHeight = Mathf.Max(maxHeight, height);
        }

        for (int layer = 1; layer <= maxHeight; layer++)
        {
            var footprint = new HashSet<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, int> entry in heights)
            {
                if (entry.Value >= layer)
                {
                    footprint.Add(entry.Key);
                }
            }

            SpawnCorners(footprint, layer);
        }
    }

    public void Clear()
    {
        if (generatedRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        generatedRoot = null;
    }

    private void GeneratePlatform(PlatformConfig config, HashSet<Vector2Int> parentCells, int parentHeight, Dictionary<Vector2Int, int> heights, System.Random placementRng)
    {
        HashSet<Vector2Int> cells = GenerateContainedBlob(parentCells, config, placementRng);
        if (cells.Count == 0)
        {
            return;
        }

        int height = parentHeight + Mathf.Max(1, config.heightStep);
        foreach (Vector2Int cell in cells)
        {
            RaiseHeight(heights, cell, height);
        }

        foreach (PlatformConfig child in config.children)
        {
            GeneratePlatform(child, cells, height, heights, placementRng);
        }
    }

    private HashSet<Vector2Int> GenerateContainedBlob(HashSet<Vector2Int> parentCells, PlatformConfig config, System.Random placementRng)
    {
        var parentList = new List<Vector2Int>(parentCells);
        const int maxAttempts = 30;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (parentList.Count == 0)
            {
                break;
            }

            Vector2Int center = parentList[placementRng.Next(parentList.Count)];
            HashSet<Vector2Int> candidate = GenerateBlob(center, config.radius, config.irregularity, config.noiseScale, config.seed + attempt);

            if (candidate.Count > 0 && candidate.IsSubsetOf(parentCells))
            {
                return candidate;
            }
        }

        LogHelper.Warn("IslandBuilder", $"Could not fit a platform (radius {config.radius}) inside its parent footprint after {maxAttempts} attempts.");
        return new HashSet<Vector2Int>();
    }

    private void ConnectIslands(HashSet<Vector2Int> a, HashSet<Vector2Int> b, Dictionary<Vector2Int, int> heights)
    {
        Vector2Int bestA = default;
        Vector2Int bestB = default;
        int bestDistance = int.MaxValue;
        bool found = false;

        foreach (Vector2Int cellA in a)
        {
            foreach (Vector2Int cellB in b)
            {
                Vector2Int delta = cellA - cellB;
                int distance = delta.x * delta.x + delta.y * delta.y;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestA = cellA;
                    bestB = cellB;
                    found = true;
                }
            }
        }

        if (!found)
        {
            return;
        }

        foreach (Vector2Int cell in WalkPath(bestA, bestB, bridgeWidth))
        {
            RaiseHeight(heights, cell, 1);
        }
    }

    private static IEnumerable<Vector2Int> WalkPath(Vector2Int start, Vector2Int end, int width)
    {
        var path = new List<Vector2Int>();
        int x = start.x;
        int z = start.y;

        while (x != end.x)
        {
            x += x < end.x ? 1 : -1;
            AddBand(path, x, z, width, horizontal: true);
        }

        while (z != end.y)
        {
            z += z < end.y ? 1 : -1;
            AddBand(path, x, z, width, horizontal: false);
        }

        return path;
    }

    private static void AddBand(List<Vector2Int> path, int x, int z, int width, bool horizontal)
    {
        int half = width / 2;
        for (int w = -half; w < width - half; w++)
        {
            path.Add(horizontal ? new Vector2Int(x, z + w) : new Vector2Int(x + w, z));
        }
    }

    private static void RaiseHeight(Dictionary<Vector2Int, int> heights, Vector2Int cell, int height)
    {
        if (!heights.TryGetValue(cell, out int existing) || existing < height)
        {
            heights[cell] = height;
        }
    }

    private HashSet<Vector2Int> GenerateBlob(Vector2Int center, int radius, float irregularity, float noiseScale, int seed)
    {
        var rng = new System.Random(seed);
        Vector2 noiseOffset = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        var cells = new HashSet<Vector2Int>();

        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                float distance = new Vector2(x, z).magnitude;
                float normalizedDistance = radius > 0 ? distance / radius : 0f;

                float noise = FractalNoise(
                    (x + noiseOffset.x) * noiseScale,
                    (z + noiseOffset.y) * noiseScale,
                    noiseOctaves, noisePersistence);

                // irregularity=0 keeps the old near-circular falloff; irregularity=1 lets the
                // noise shape the coastline almost freely, only loosely bounded by radius.
                float falloff = normalizedDistance * normalizedDistance * (1f - irregularity);
                float value = noise - falloff;

                if (value > 0.5f)
                {
                    cells.Add(new Vector2Int(center.x + x, center.y + z));
                }
            }
        }

        return cells;
    }

    private static float FractalNoise(float x, float z, int octaves, float persistence)
    {
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return total / maxValue;
    }

    private void SpawnGround(Vector2Int cell, int height)
    {
        Vector3 position = new Vector3(cell.x * cellSize, height * cellHeight * 0.5f, cell.y * cellSize);
        SpawnAt(groundPrefab, position, Quaternion.identity, new Vector3(1f, height, 1f));
    }

    private void SpawnCorners(HashSet<Vector2Int> footprint, int layer)
    {
        var emptyNeighbors = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in footprint)
        {
            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int neighbor = cell + direction;
                if (!footprint.Contains(neighbor))
                {
                    emptyNeighbors.Add(neighbor);
                }
            }
        }

        foreach (Vector2Int empty in emptyNeighbors)
        {
            foreach ((Vector2Int a, Vector2Int b, float angle) in CornerPairs)
            {
                if (footprint.Contains(empty + a) && footprint.Contains(empty + b))
                {
                    Vector3 position = new Vector3(empty.x * cellSize, (layer - 0.5f) * cellHeight, empty.y * cellSize);
                    Quaternion rotation = Quaternion.Euler(0f, angle + edgeYawOffset, 0f);
                    SpawnAt(groundEdgePrefab, position, rotation, Vector3.one);
                }
            }
        }
    }

    private void SpawnAt(GameObject prefab, Vector3 localPosition, Quaternion rotation, Vector3 scale)
    {
        if (prefab == null)
        {
            return;
        }

        GameObject instance =
#if UNITY_EDITOR
            Application.isPlaying ? Instantiate(prefab) : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
#else
            Instantiate(prefab);
#endif

        instance.transform.SetParent(generatedRoot, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = rotation;
        instance.transform.localScale = scale;
    }
}
