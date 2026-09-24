using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The model set a TilesetPlatformBuilder draws from. Models are authored 1 unit tall with the pivot
// at the bottom center of their tile; the builder scales Y to the cube height.
//   DualGrid: 4 keys (Center, Edge, Corner, InnerCorner), tiles centered on grid vertices.
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
    }

    [SerializeField] private TilesetAutotiler.Mode tilingMode = TilesetAutotiler.Mode.PerCell;

    [SerializeField, Tooltip("Stretch one Center over each rectangle of plain center tiles instead of one per tile.")]
    private bool mergeCenters = true;

    [SerializeField, Tooltip("Model asset names are <prefix><key>, e.g. Outpost_Edge.")]
    private string modelPrefix = "Outpost_";

    [SerializeField, Tooltip("Place the 2x2 rounded inner corner where the layout allows it. Off = sharp per-cell inner corners everywhere.")]
    private bool useInnerCorners = true;

    [SerializeField] private List<Entry> pieces = new();

    public bool UseInnerCorners => useInnerCorners;
    public bool MergeCenters => mergeCenters;
    public TilesetAutotiler.Mode TilingMode => tilingMode;

    public GameObject Get(string key) => Get(key, 0);

    // Weighted, deterministic variant pick: the same hash always returns the same model.
    public GameObject Get(string key, int hash)
    {
        var total = 0f;
        foreach (var entry in pieces)
        {
            if (entry.Key == key && entry.Model != null)
                total += entry.Weight > 0f ? entry.Weight : 1f;
        }

        if (total <= 0f)
            return null;

        var roll = (hash & 0x7fffffff) / (float)int.MaxValue * total;
        GameObject last = null;
        foreach (var entry in pieces)
        {
            if (entry.Key != key || entry.Model == null)
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
        }
        else
        {
            keys.Add(TilesetAutotiler.InnerCornerKey);
            foreach (var piece in TilesetAutotiler.CellPieces)
                keys.Add(piece.Key);
        }

        var oldWeights = new Dictionary<GameObject, float>();
        foreach (var entry in pieces)
        {
            if (entry.Model != null)
                oldWeights[entry.Model] = entry.Weight;
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
            if (found.Count == 0)
                missing.Add(key);
            foreach (var model in found)
                pieces.Add(new Entry { Key = key, Model = model, Weight = oldWeights.TryGetValue(model, out var w) ? w : 1f });
        }

        EditorUtility.SetDirty(this);
        if (missing.Count > 0)
            QuantumUser.View.Util.LogHelper.Warn("Tileset", $"{name}: no model found for {string.Join(", ", missing)}", this);
        else
            QuantumUser.View.Util.LogHelper.Log("Tileset", $"{name}: filled {pieces.Count} pieces from {folder}", this);
    }
#endif
}
