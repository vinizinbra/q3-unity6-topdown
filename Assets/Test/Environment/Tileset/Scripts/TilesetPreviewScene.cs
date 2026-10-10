using System.Collections.Generic;
using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Drives TilesetPreview.unity: look at a biome exactly the way the game shows it (same camera, light,
// water and EnvironmentManager as the game scene) WITHOUT running a match. Pick a Theme and press
// Apply - it loads the theme through the real EnvironmentManager (tileset, sky, water colours / surface
// material, blood), builds every tileset cube in the scene and bakes the water shore field.
//
// The shore field is normally baked by WaterShoreBaker from the Quantum frame (chunks + collider
// probes), which doesn't exist in Edit mode; this bakes the same field (same mapping, same encoding,
// same global shader params) from the scene's tileset cubes instead, so edge fades / shore foam match.
[ExecuteAlways]
public class TilesetPreviewScene : MonoBehaviour
{
    private static readonly int ShoreFieldId = Shader.PropertyToID("_ShoreField");
    private static readonly int ShoreFieldParamsId = Shader.PropertyToID("_ShoreFieldParams");

    [SerializeField, Expandable] private WorldTheme theme;
    [SerializeField] private EnvironmentManager environment;

    [Header("Shore field (keep in sync with the game scene's WaterShoreBaker)")]
    [SerializeField] private float worldExtent = 150f;
    [SerializeField] private Vector2 worldCenter = Vector2.zero;
    [SerializeField] private float worldUnitsPerTexel = 0.2f;
    [SerializeField] private float maxShoreDistanceWorld = 6f;

    private Texture2D field;

    private const string LogTag = "TilesetPreview";

    private void OnEnable()
    {
#if UNITY_EDITOR
        // not from OnEnable itself: building spawns objects, which Unity refuses during scene load
        if (!Application.isPlaying)
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                    Apply();
            };
#endif
    }

    // Play mode: the cubes build themselves (autoGenerate) and EnvironmentManager loads its initial theme.
    private void Start()
    {
        if (Application.isPlaying)
            BakeShoreField();
    }

    [Button("Apply Theme")]
    public void Apply()
    {
        if (environment != null && theme != null)
        {
#if UNITY_EDITOR
            // so Play mode (EnvironmentManager.Awake) shows the same theme
            var so = new UnityEditor.SerializedObject(environment);
            var initial = so.FindProperty("initialTheme");
            if (initial.objectReferenceValue != theme)
            {
                initial.objectReferenceValue = theme;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
#endif
            environment.Load(theme);
        }

        BuildAll();
        BakeShoreField();
    }

    [Button("Rebuild Tiles")]
    public void BuildAll()
    {
        var candidates = TilesetPlatformBuilder.FindCandidates(gameObject.scene);
        var done = new HashSet<TilesetPlatformBuilder>();
        foreach (var cube in candidates)
        {
            if (done.Contains(cube))
                continue;
            cube.Generate(candidates);
            foreach (var member in cube.Members)
                done.Add(member);
            done.Add(cube);
        }
    }

    [Button("Bake Shore Field")]
    public void BakeShoreField()
    {
        var res = Mathf.Max(Mathf.CeilToInt(worldExtent * 2f / worldUnitsPerTexel), 1);
        var land = new bool[res * res];
        var any = false;
        foreach (var cube in TilesetPlatformBuilder.FindCandidates(gameObject.scene))
        {
            var box = cube.GetComponent<BoxCollider>();
            if (box == null)
                continue;
            var b = box.bounds;
            var x0 = Mathf.Clamp(Mathf.RoundToInt((b.min.x - worldCenter.x) / worldUnitsPerTexel + res * 0.5f), 0, res);
            var x1 = Mathf.Clamp(Mathf.RoundToInt((b.max.x - worldCenter.x) / worldUnitsPerTexel + res * 0.5f), 0, res);
            var y0 = Mathf.Clamp(Mathf.RoundToInt((b.min.z - worldCenter.y) / worldUnitsPerTexel + res * 0.5f), 0, res);
            var y1 = Mathf.Clamp(Mathf.RoundToInt((b.max.z - worldCenter.y) / worldUnitsPerTexel + res * 0.5f), 0, res);
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                land[y * res + x] = true;
            any = true;
        }

        if (!any)
        {
            LogHelper.Warn(LogTag, "No tileset cubes to bake a shore field from.", this);
            return;
        }

        ShoreObstacles.Stamp(land, res, worldUnitsPerTexel, worldCenter);   // icebergs etc. count as land
        var dist = ChamferDistance(land, res);
        if (field == null || field.width != res)
        {
            if (field != null)
                DestroyImmediate(field);
            field = new Texture2D(res, res, TextureFormat.R8, false)
            {
                name = "TilesetPreviewShoreField",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        // same encoding as WaterShoreBaker: R = saturate(worldDistanceToLand / maxShoreDistanceWorld)
        var px = new byte[res * res];
        var invMax = 1f / Mathf.Max(maxShoreDistanceWorld, 0.001f);
        for (var i = 0; i < px.Length; i++)
            px[i] = (byte)(Mathf.Clamp01(dist[i] * worldUnitsPerTexel * invMax) * 255f);
        field.SetPixelData(px, 0);
        field.Apply(false);

        Shader.SetGlobalTexture(ShoreFieldId, field);
        Shader.SetGlobalVector(ShoreFieldParamsId, new Vector4(worldCenter.x, worldCenter.y, worldExtent * 2f, maxShoreDistanceWorld));
    }

    // Two-pass (1, sqrt2) chamfer distance to the nearest land texel, in texels (as WaterShoreBaker).
    private static float[] ChamferDistance(bool[] land, int res)
    {
        const float orth = 1f;
        const float diag = 1.41421356f;
        var d = new float[res * res];
        for (var i = 0; i < d.Length; i++)
            d[i] = land[i] ? 0f : res * 2f;

        for (var y = 0; y < res; y++)
        for (var x = 0; x < res; x++)
        {
            var i = y * res + x;
            var v = d[i];
            if (v == 0f)
                continue;
            if (x > 0) v = Mathf.Min(v, d[i - 1] + orth);
            if (y > 0)
            {
                v = Mathf.Min(v, d[i - res] + orth);
                if (x > 0) v = Mathf.Min(v, d[i - res - 1] + diag);
                if (x < res - 1) v = Mathf.Min(v, d[i - res + 1] + diag);
            }
            d[i] = v;
        }

        for (var y = res - 1; y >= 0; y--)
        for (var x = res - 1; x >= 0; x--)
        {
            var i = y * res + x;
            var v = d[i];
            if (v == 0f)
                continue;
            if (x < res - 1) v = Mathf.Min(v, d[i + 1] + orth);
            if (y < res - 1)
            {
                v = Mathf.Min(v, d[i + res] + orth);
                if (x < res - 1) v = Mathf.Min(v, d[i + res + 1] + diag);
                if (x > 0) v = Mathf.Min(v, d[i + res - 1] + diag);
            }
            d[i] = v;
        }

        return d;
    }

    private void OnDestroy()
    {
        if (field != null)
            DestroyImmediate(field);
    }
}
