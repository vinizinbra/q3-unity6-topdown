using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The model set a TilesetPlatformBuilder draws from. Models are authored 1 unit tall with the pivot
// at the bottom center of their tile; the builder scales Y to the cube height.
//   DualGrid: 4 keys (Center, Edge, Corner, InnerCorner), tiles centered on grid vertices, plus
//             optional Edge2 (2-long straight wall, pivot between two vertices) merged along runs.
//   PerCell : 15 cell keys + the 2x2 InnerCorner (see TilesetAutotiler).
// A key may have several entries (variants, e.g. Outpost_Edge_V1..V6); the builder picks one per
// tile with a stable hash, weighted by Weight.
[CreateAssetMenu(menuName = "RiftRaiders/Test/Tileset Definition", fileName = "TilesetDefinition")]
public class TilesetDefinition : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string Key;
        public GameObject Model;
        [Tooltip("Relative chance among variants of the same key (<= 0 counts as 1).")]
        public float Weight;
        [Tooltip("Never used stretched over a longer wall segment (e.g. broken-lip notches that would open a walk-on-air gap when widened).")]
        public bool NoStretch;
    }

    [SerializeField] private TilesetAutotiler.Mode tilingMode = TilesetAutotiler.Mode.PerCell;

    [SerializeField, Tooltip("Stretch one Center over each rectangle of plain center tiles instead of one per tile.")]
    private bool mergeCenters = true;

    [SerializeField, Tooltip("The one material every model of this set renders with (ToonTerrain). EnvironmentManager writes a world's water depth/line colours into it. Empty = taken from the first model's renderer.")]
    private Material material;

    [SerializeField, Tooltip("Model asset names are <prefix><key>, e.g. Outpost_Edge.")]
    private string modelPrefix = "Outpost_";

    [SerializeField, Tooltip("Place the 2x2 rounded inner corner where the layout allows it. Off = sharp per-cell inner corners everywhere.")]
    private bool useInnerCorners = true;

    [SerializeField, Tooltip("DualGrid: longest straight wall segment (in cells) one stretched edge piece may cover. 1 = no stretching.")]
    private int maxEdgeLength = 3;

    [SerializeField, Tooltip("DualGrid: max along-wall stretch of an edge model (details widen by this much). Edge2 is native 2 cells, Edge 1.")]
    private float maxEdgeStretch = 1.5f;

    [SerializeField] private List<Entry> pieces = new();

    public bool UseInnerCorners => useInnerCorners;
    public bool MergeCenters => mergeCenters;
    public TilesetAutotiler.Mode TilingMode => tilingMode;

    public TilesetAutotiler.EdgeRunOptions EdgeRuns => new()
    {
        HasLong = Has(TilesetAutotiler.EdgeLongKey),
        MaxLength = Mathf.Max(1, maxEdgeLength),
        MaxStretch = Mathf.Max(1f, maxEdgeStretch),
    };

    public Material Material
    {
        get
        {
            if (material != null)
                return material;

            foreach (var entry in pieces)
            {
                var r = entry.Model != null ? entry.Model.GetComponentInChildren<Renderer>() : null;
                if (r != null && r.sharedMaterial != null)
                    return r.sharedMaterial;
            }

            return null;
        }
    }

    public GameObject Get(string key) => Get(key, 0);

    public bool Has(string key)
    {
        foreach (var entry in pieces)
        {
            if (entry.Key == key && entry.Model != null)
                return true;
        }

        return false;
    }

    // Weighted, deterministic variant pick: the same hash always returns the same model.
    // stretched = the piece will be scaled along its wall: NoStretch variants are skipped (unless
    // nothing else exists for the key).
    public GameObject Get(string key, int hash, bool stretched = false)
    {
        var total = 0f;
        foreach (var entry in pieces)
        {
            if (entry.Key == key && entry.Model != null && !(stretched && entry.NoStretch))
                total += entry.Weight > 0f ? entry.Weight : 1f;
        }

        if (total <= 0f)
            return stretched ? Get(key, hash) : null;

        var roll = (hash & 0x7fffffff) / (float)int.MaxValue * total;
        GameObject last = null;
        foreach (var entry in pieces)
        {
            if (entry.Key != key || entry.Model == null || (stretched && entry.NoStretch))
                continue;
            last = entry.Model;
            roll -= entry.Weight > 0f ? entry.Weight : 1f;
            if (roll <= 0f)
                return entry.Model;
        }

        return last;
    }

#if UNITY_EDITOR
    // Fills every key of the current mode from models named <prefix><key> or <prefix><key>_V<n>
    // in this asset's folder (and subfolders). Keeps the weights of models that were already listed.
    [Button("Auto Fill From Folder")]
    private void AutoFillFromFolder()
    {
        var folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(this))?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
            return;

        var keys = new List<string>();
        if (tilingMode == TilesetAutotiler.Mode.DualGrid)
        {
            foreach (var (key, _) in TilesetAutotiler.DualPieces)
                keys.Add(key);
            keys.Add(TilesetAutotiler.EdgeLongKey); // optional: 2-long straight walls
        }
        else
        {
            keys.Add(TilesetAutotiler.InnerCornerKey);
            foreach (var piece in TilesetAutotiler.CellPieces)
                keys.Add(piece.Key);
        }

        var oldEntries = new Dictionary<GameObject, Entry>();
        foreach (var entry in pieces)
        {
            if (entry.Model != null)
                oldEntries[entry.Model] = entry;
        }

        pieces.Clear();
        var missing = new List<string>();
        foreach (var key in keys)
        {
            var variant = new System.Text.RegularExpressions.Regex($"^{System.Text.RegularExpressions.Regex.Escape(modelPrefix + key)}(_V\\d+)?$");
            var found = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets($"{modelPrefix}{key} t:GameObject", new[] { folder }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && variant.IsMatch(candidate.name) && !found.Contains(candidate))
                    found.Add(candidate);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (found.Count == 0 && key != TilesetAutotiler.EdgeLongKey)
                missing.Add(key);
            foreach (var model in found)
                pieces.Add(oldEntries.TryGetValue(model, out var old)
                    ? new Entry { Key = key, Model = model, Weight = old.Weight, NoStretch = old.NoStretch }
                    : new Entry { Key = key, Model = model, Weight = 1f });
        }

        EditorUtility.SetDirty(this);
        if (missing.Count > 0)
            QuantumUser.View.Util.LogHelper.Warn("Tileset", $"{name}: no model found for {string.Join(", ", missing)}", this);
        else
            QuantumUser.View.Util.LogHelper.Log("Tileset", $"{name}: filled {pieces.Count} pieces from {folder}", this);
    }
#endif
}
