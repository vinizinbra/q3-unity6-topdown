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

    [Serializable]
    public struct ScatterEntry
    {
        public GameObject Model;
        [Tooltip("Relative chance among the entries of the same list (<= 0 counts as 1).")]
        public float Weight;
        [Tooltip("Uniform scale range (0,0 = 1).")]
        public Vector2 ScaleRange;
        [Tooltip("Free yaw + small position jitter (rocks, tufts). Off = yaw in 90 degree steps, centred (containers, machines).")]
        public bool AnyYaw;
        [Range(1, 2), Tooltip("Cells the prop needs (2 = long prop like a container; needs a free neighbour cell).")]
        public int Cells;
        [Tooltip("Wall props only: stands at the wall FOOT (platform bottom, only where that ground is visible) instead of being embedded in the face.")]
        public bool Foot;
        [Tooltip("Wall runs only: the module used on SIDE walls (its baked outline is drawn for that view). Empty = Model.")]
        public GameObject SideModel;
        [Tooltip("Foot props that stand clear of the wall (lamp poles, cones, tents): ignore the wall's avoid band, so they may be taller than the wall body.")]
        public bool FreeStanding;
        [Tooltip("Wall props only: only on walls facing the camera (flat decals - runes, paint, drips - read edge-on and look buried on side walls).")]
        public bool FrontOnly;
        [Tooltip("Wall props only: only on walls that drop into the water / void (wall bottom below 0 - base platforms, never raised blocks), placed right at the water line (model height 0 = the water surface). E.g. a sewage outlet pouring into the canal.")]
        public bool WaterEdge;
    }

    [Header("Surface scatter (props on top of platforms, see TilesetPlatformBuilder.SurfaceDecor)")]
    [SerializeField, Tooltip("Small walk-through decoration for walkable floors: rocks, tufts, cables. Nothing that looks solid.")]
    private List<ScatterEntry> groundScatter = new();
    [SerializeField, Range(0f, 1f), Tooltip("Chance per interior cell of a walkable floor to get one ground prop.")]
    private float groundDensity = 0.06f;
    [SerializeField, Tooltip("Props for raised blocks players can't walk on: containers, machines, antennas, crates...")]
    private List<ScatterEntry> rooftopScatter = new();
    [SerializeField, Range(0f, 1f), Tooltip("Chance per interior cell of a raised block to get one rooftop prop.")]
    private float rooftopDensity = 0.3f;
    [SerializeField, Tooltip("Props placed along a raised block's edges (e.g. fence segments), 1 unit long along local X, facing like the edge tile.")]
    private List<ScatterEntry> rooftopEdgeProps = new();
    [SerializeField, Range(0f, 1f), Tooltip("Chance per edge tile of a raised block to get an edge prop.")]
    private float rooftopEdgeChance = 0.5f;

    [SerializeField, Tooltip("Props embedded in wall faces (tyres, skulls, pipes...) or standing at the wall foot (Foot). Authored facing -Z, pivot at the mount point, lowest point at height 0. Real size - not stretched with the wall.")]
    private List<ScatterEntry> wallScatter = new();
    [SerializeField, Range(0f, 1f), Tooltip("Chance per wall edge tile to get one wall prop.")]
    private float wallDensity = 0.3f;

    [SerializeField, Tooltip("Band of the wall (fraction of wall height, 0 = foot, 1 = top) wall props must never overlap - e.g. a neon trim baked into the wall profile. (0,0) = none.")]
    private Vector2 wallPropAvoidBand = Vector2.zero;
    [SerializeField, Tooltip("Extra world-unit gap kept between a prop and the avoid band.")]
    private float wallPropAvoidMargin = 0.04f;

    [SerializeField, Tooltip("Extra world-unit push INTO the wall for embedded wall props (for profiles whose wall body is recessed under a cap, e.g. NeonCityV5).")]
    private float wallPropInset = 0f;

    [SerializeField, Tooltip("How much deeper (world units, at the wall foot) embedded wall props / runs sit than at the top - follows an undercut cliff face that tucks in toward its foot. 0 for straight built walls (Neo-Favela), so flat props like graffiti stay on the face.")]
    private float wallPropSlope = 0.12f;

    // A continuous modular run along a straight wall (pipeline, cable tray, fence...): 1-cell modules
    // laid side by side at one height, capped at both ends. Modules are authored 1 unit long along X
    // (-0.5..0.5), facing -Z, pivot on the wall face; End pieces are authored as the run's +X end and
    // mirrored for the other one. Runs follow the wall around corners that have a Corner / InnerCorner
    // module (never onto walls facing away from the camera).
    [Serializable]
    public class WallRunSet
    {
        public string Name;
        [Tooltip("Plain middle modules (weighted).")]
        public List<ScatterEntry> Straight = new();
        [Tooltip("Decorated middle modules - valves, gauges, boxes (weighted).")]
        public List<ScatterEntry> Decorated = new();
        [Range(0f, 1f), Tooltip("Chance a middle cell uses a Decorated module.")]
        public float DecoratedChance = 0.35f;
        [Min(0), Tooltip("Minimum plain cells between two Decorated modules.")]
        public int DecoratedSpacing = 2;
        [Tooltip("End that turns into the wall / a wall box (+X end).")]
        public GameObject EndWall;
        public GameObject EndWallSide;
        [Tooltip("End that turns DOWN (+X end); its downward part ends at the model pivot height, at DropX.")]
        public GameObject EndDown;
        public GameObject EndDownSide;
        [Tooltip("Vertical module under EndDown, authored 0..1 tall at DropX and stretched to reach the ground.")]
        public GameObject Drop;
        public GameObject DropSide;
        [Tooltip("Optional base where the Drop meets the ground (flange, sand heap).")]
        public GameObject DropFoot;
        [Tooltip("Wraps a convex corner, authored in the Corner tile's frame (NE quarter solid, in from +X, out to +Z). Empty = runs stop at convex corners.")]
        public GameObject Corner;
        [Tooltip("Corner variant used when the corner's -X wall (not the -Z one) faces the camera (baked outline for that view). Empty = Corner.")]
        public GameObject CornerXFront;
        [Tooltip("Follows a concave corner, authored in the InnerCorner tile's frame (SW quarter empty, in from -X, out to -Z). Empty = runs stop at inner corners.")]
        public GameObject InnerCorner;
        public GameObject InnerCornerXFront;

        // "_Side" / "XFront" variants carry the baked outline for side walls; fall back to the base module
        public static GameObject Pick(GameObject front, GameObject side, bool isSide) => isSide && side != null ? side : front;
        [Tooltip("Where the Drop sits along X in the End module's frame (for the ground check).")]
        public float DropX = 0.3f;
        [Range(0f, 1f), Tooltip("Chance an end turns down to the ground (only where there is ground); otherwise EndWall.")]
        public float DownEndChance = 0.7f;
        [Tooltip("Authored height of the run's centreline above the module pivot.")]
        public float CenterHeight = 0.2f;
        [Tooltip("Run centreline this far below the platform top (world units).")]
        public float BelowTop = 0.45f;
        [Tooltip("With a tileset avoid band (e.g. NeonCityV5's cap slab), the centreline also stays at least this far below the band's lower edge (world units).")]
        public float BandClearance = 0.3f;
        [Tooltip("Ignore the tileset's avoid band: the run sits on / spills over the cap itself (vines draping over a laje).")]
        public bool OverBand;
        [Tooltip("Walls lower than this (top - bottom) get no run.")]
        public float MinWallHeight = 0.9f;
        [Range(0f, 1f), Tooltip("Chance per free stretch of a camera-facing straight wall to start a run (half on side walls).")]
        public float Chance = 0.5f;
        [Tooltip("Run length range in cells (ends included).")]
        public Vector2Int Length = new(3, 8);
        [Min(1), Tooltip("Minimum empty cells between two runs on the same wall.")]
        public int MinGap = 2;
        [Tooltip("Extra push into the wall (world units).")]
        public float Inset;
        [Tooltip("Modules stand on the ground in front of the wall (street lights every few cells...) instead of on the wall face: pivot at the wall foot, only where there is ground; the wall height / band settings are ignored. Leave Straight's model empty and use Decorated (+ DecoratedSpacing) for evenly spaced items.")]
        public bool Foot;
        [Tooltip("Only on walls facing the camera (flat things read edge-on on side walls - clotheslines).")]
        public bool FrontOnly;
        [Tooltip("Foot runs: how high above the wall foot a spawned item blocks the wall props of its cell (a low sandbag trench leaves room for props above it; a pole blocks the whole cell).")]
        public float FootReserveHeight = 99f;
    }

    // One big decal-like piece on a walkable floor's open centre (a football pitch, helipad, plaza...): placed on the
    // free Size rectangle closest to the platform centre, at most once per platform, Ground platforms only.
    [Serializable]
    public class GroundFeature
    {
        public string Name;
        [Tooltip("Authored centred on its pivot, Size.x units along X and Size.y along Z (1 unit = 1 cell).")]
        public GameObject Model;
        [Tooltip("Footprint in cells (X by Z). The builder also tries it rotated 90 degrees.")]
        public Vector2Int Size = new(7, 4);
        [Min(0), Tooltip("Free cells required around the footprint (keeps it off the walls).")]
        public int Margin = 1;
        [Range(0f, 1f), Tooltip("Chance per platform that has room for it.")]
        public float Chance = 0.6f;
    }

    [SerializeField, Tooltip("Big floor pieces for open areas of walkable floors (football pitch...). Ground platforms only.")]
    private List<GroundFeature> groundFeatures = new();

    public IReadOnlyList<GroundFeature> GroundFeatures => groundFeatures;

    [SerializeField, Tooltip("Continuous modular runs along straight walls (pipelines, cables, fences). Wall props keep out of the cells/height they cover.")]
    private List<WallRunSet> wallRuns = new();

    public IReadOnlyList<WallRunSet> WallRuns => wallRuns;
    public float WallPropInset => wallPropInset;
    public float WallPropSlope => wallPropSlope;
    public Vector2 WallPropAvoidBand => wallPropAvoidBand;
    public float WallPropAvoidMargin => wallPropAvoidMargin;
    public IReadOnlyList<ScatterEntry> WallScatter => wallScatter;
    public float WallDensity => wallDensity;
    public IReadOnlyList<ScatterEntry> GroundScatter => groundScatter;
    public float GroundDensity => groundDensity;
    public IReadOnlyList<ScatterEntry> RooftopScatter => rooftopScatter;
    public float RooftopDensity => rooftopDensity;
    public IReadOnlyList<ScatterEntry> RooftopEdgeProps => rooftopEdgeProps;
    public float RooftopEdgeChance => rooftopEdgeChance;

    // Weighted, deterministic pick from a scatter list (null entry = empty list).
    public static bool PickScatter(IReadOnlyList<ScatterEntry> list, int hash, out ScatterEntry picked)
    {
        picked = default;
        var total = 0f;
        foreach (var e in list)
        {
            if (e.Model != null)
                total += e.Weight > 0f ? e.Weight : 1f;
        }

        if (total <= 0f)
            return false;

        var roll = (hash & 0x7fffffff) / (float)int.MaxValue * total;
        foreach (var e in list)
        {
            if (e.Model == null)
                continue;
            picked = e;
            roll -= e.Weight > 0f ? e.Weight : 1f;
            if (roll <= 0f)
                return true;
        }

        return picked.Model != null;
    }

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
