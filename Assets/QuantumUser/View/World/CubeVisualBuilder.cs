using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// Assumes each cube's pivot sits at its bottom min corner (min X, min Y, min Z) - e.g. a
// ProBuilder box drawn from a corner - not a center-pivot primitive cube. All position/bounds
// math below derives the box's center or bottom-face center from that corner accordingly.
//
// Assumes every prefab below is authored so that parenting it at local position (0,0,0) and
// scale (1,1,1) makes it exactly match a plain 1x1x1 cube with that same pivot convention -
// so placement below never has to correct for pivot or bounds, only pick, rotate and position.
public class CubeVisualBuilder : MonoBehaviour
{
    [SerializeField] private List<GameObject> edgePrefabs;
    [SerializeField] private float edgeYaw;

    [Tooltip("Extra length (world units) added on top of an edge piece's along-the-wall stretch, so a run of N cells scales to N + this instead of exactly N - closes small seams between neighboring pieces.")]
    [SerializeField] private float edgeScaleOverlap = 0.2f;

    [SerializeField] private List<GameObject> outerCornerPrefabs;
    [SerializeField] private float outerCornerYaw;

    [SerializeField] private List<GameObject> centerPrefabs;
    [SerializeField] private float centerYaw;

    [Tooltip("Shrink the single center piece by this much (world units, split across both sides) so it sits just inside the edge/corner ring instead of poking past it.")]
    [SerializeField] private float centerScaleMargin = 1.9f;

    [Tooltip("Concave connector piece used where another CubeVisualBuilder touches this corner. Leave empty to skip inner corners entirely.")]
    [SerializeField] private List<GameObject> innerCornerPrefabs;
    [SerializeField] private float innerCornerYaw;

    [Tooltip("Two same-height cubes whose facing sides are within this distance (world units) but don't actually overlap still read as one open space: the edge run facing the gap opens into a floor piece instead of a wall, though their outer corners stay as corners.")]
    [SerializeField] private float touchGapTolerance = 0.1f;

    [SerializeField, HideInInspector] private Transform colliderRoot;
    [SerializeField, HideInInspector] private Transform visualRoot;

    // Shared across every cube in the scene: a cube that gets absorbed as another cube's merging
    // neighbor has its whole grid drawn by that host (see DrawMergingNeighbors) and its own box
    // hidden, so it must not run its own Generate() too - that would draw the same cells twice.
    private static readonly HashSet<CubeVisualBuilder> consumedByMerge = new HashSet<CubeVisualBuilder>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetGenerationState()
    {
        consumedByMerge.Clear();
    }
    // Start() (not Awake()) so this only runs once every cube instantiated this frame already
    // exists - Unity defers Start() on newly instantiated objects until after Awake() has run for
    // everything created that frame, so by the time any cube's Start() fires, FindMergeCluster()
    // can already see every other cube in its merge group, however many frames or Instantiate()
    // calls it took to spawn them all. Whichever cube's Start() happens to run first claims the
    // whole cluster via consumedByMerge; every other member's own Start() then sees itself already
    // in there and does nothing.
    public void Start()
    {
        if (consumedByMerge.Contains(this))
        {
            return;
        }

        Generate();

        foreach (CubeVisualBuilder member in FindMergeCluster())
        {
            consumedByMerge.Add(member);
        }
    }

#if UNITY_EDITOR
    // Corner/edge pieces and the per-cell grid assume the cube sits on whole-unit grid positions
    // with a whole-unit size - a fractional value would leave seams or misaligned pieces. While a
    // transform handle is actively being dragged (GUIUtility.hotControl != 0) this only shows a
    // red indicator; it snaps to the nearest grid values once the handle is released, so it
    // doesn't fight the drag mid-motion.
    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            return;
        }

        bool onGrid = IsWholeUnits(transform.position) && IsWholeUnits(transform.localScale);

        if (onGrid)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position + transform.localScale * 0.5f, transform.localScale);

        if (GUIUtility.hotControl != 0)
        {
            return;
        }

        UnityEditor.Undo.RecordObject(transform, "Snap CubeVisualBuilder To Grid");
        transform.position = Rounded(transform.position);
        transform.localScale = Rounded(transform.localScale);
        Debug.Log($"[CubeVisualBuilder] Snapped to grid - position {transform.position}, scale {transform.localScale}.", this);
    }

    private static Vector3 Rounded(Vector3 v)
    {
        return new Vector3(Mathf.Round(v.x), Mathf.Round(v.y), Mathf.Round(v.z));
    }

    private static bool IsWholeUnits(Vector3 v)
    {
        const float epsilon = 0.01f;
        return Mathf.Abs(v.x - Mathf.Round(v.x)) < epsilon
            && Mathf.Abs(v.y - Mathf.Round(v.y)) < epsilon
            && Mathf.Abs(v.z - Mathf.Round(v.z)) < epsilon;
    }
#endif

    [Button]
    public void Generate()
    {
        int gridX = Mathf.RoundToInt(transform.localScale.x);
        int gridZ = Mathf.RoundToInt(transform.localScale.z);

        if (gridX < 2 || gridZ < 2)
        {
            Debug.LogWarning($"[CubeVisualBuilder] Cube must be at least 2xNx2 in scale (got {gridX}x{gridZ}) - skipping.", this);
            return;
        }

        // Tear down this cube's whole current merge cluster before drawing anything. ClearVisuals()
        // below only clears this cube's own visualRoot - if a neighbor is still holding an older
        // drawn copy of this same cluster (e.g. Generate() was called directly on a cube that had
        // already been absorbed as someone else's merging neighbor, bypassing the consumedByMerge
        // check that only guards the automatic Start() path), that stale copy would otherwise sit
        // there permanently, doubled up with whatever this call draws.
        ResetCube();

        WarnIfNotWholeUnits(gridX, gridZ);
        EnsureStructure();

        visualRoot.localScale = new Vector3(1f / gridX, 1f, 1f / gridZ);

        ClearVisuals();

        List<CubeVisualBuilder> mergingNeighbors = FindMergingNeighbors();
        List<CubeVisualBuilder> mergeCluster = FindMergeCluster();
        List<Vector3> consumedPositions = CollectConsumedPositions(mergeCluster);

        PlaceGrid(gridX, gridZ, visualRoot, mergingNeighbors, FindTouchingNeighbors(), claimedBounds: null, consumedPositions);
        DrawMergingNeighbors(mergeCluster, consumedPositions);
    }

    // Every cube transitively connected to this one through direct overlaps (A overlaps B, B
    // overlaps C, even if A and C don't directly overlap) - a plain BFS over FindMergingNeighbors.
    // Generate()/Awake() need the whole chain, not just direct neighbors, otherwise a 3+ cube chain
    // can end up with a cube processed twice (once as another's "direct neighbor", once via its own
    // Awake()) or never notch-scanned at all (see CollectConsumedPositions).
    private List<CubeVisualBuilder> FindMergeCluster()
    {
        List<CubeVisualBuilder> cluster = new List<CubeVisualBuilder>();
        HashSet<CubeVisualBuilder> visited = new HashSet<CubeVisualBuilder> { this };
        Queue<CubeVisualBuilder> frontier = new Queue<CubeVisualBuilder>();
        frontier.Enqueue(this);

        while (frontier.Count > 0)
        {
            foreach (CubeVisualBuilder neighbor in frontier.Dequeue().FindMergingNeighbors())
            {
                if (visited.Add(neighbor))
                {
                    cluster.Add(neighbor);
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return cluster;
    }

    // Merging neighbors don't get their own separate Generate() call - instead their grid is
    // drawn as children of this cube's visualRoot, and their own box mesh/collider is hidden,
    // so two merged cubes read as one continuous object instead of two overlapping ones.
    // Cells inside bounds already claimed by an earlier cube (this one, or an earlier neighbor)
    // are skipped so the overlapping region between two cubes isn't drawn twice.
    private void DrawMergingNeighbors(List<CubeVisualBuilder> mergeCluster, List<Vector3> consumedPositions)
    {
        List<Bounds> claimedBounds = new List<Bounds> { GetWorldBounds() };

        foreach (CubeVisualBuilder neighbor in mergeCluster)
        {
            neighbor.HideOwnBoxVisuals();

            int neighborGridX = Mathf.RoundToInt(neighbor.transform.localScale.x);
            int neighborGridZ = Mathf.RoundToInt(neighbor.transform.localScale.z);
            if (neighborGridX < 2 || neighborGridZ < 2)
            {
                continue;
            }

            Transform adoptedVisualRoot = CreateAdoptedVisualRoot(neighbor, neighborGridX, neighborGridZ);
            neighbor.PlaceGrid(neighborGridX, neighborGridZ, adoptedVisualRoot, neighbor.FindMergingNeighbors(), neighbor.FindTouchingNeighbors(), claimedBounds, consumedPositions);
            claimedBounds.Add(neighbor.GetWorldBounds());
        }
    }

    // Pre-scans every cell of this cube and every cube in its merge cluster (not just this cube's
    // own grid) for inner corners, and collects the two flanking cell positions each one consumes -
    // the L-shaped connector piece visually covers those too, so they shouldn't also get a plain
    // edge piece placed on top of it. Done globally up front since a notch detected while scanning
    // one cube can consume a flanking cell that belongs to a different cube's own grid.
    private List<Vector3> CollectConsumedPositions(List<CubeVisualBuilder> mergeCluster)
    {
        List<Vector3> consumed = new List<Vector3>();

        CollectConsumedPositionsForCube(this, FindMergingNeighbors(), consumed);
        foreach (CubeVisualBuilder member in mergeCluster)
        {
            CollectConsumedPositionsForCube(member, member.FindMergingNeighbors(), consumed);
        }

        return consumed;
    }

    private static void CollectConsumedPositionsForCube(CubeVisualBuilder cube, List<CubeVisualBuilder> cubeMergingNeighbors, List<Vector3> consumed)
    {
        int gridX = Mathf.RoundToInt(cube.transform.localScale.x);
        int gridZ = Mathf.RoundToInt(cube.transform.localScale.z);
        if (gridX < 2 || gridZ < 2)
        {
            return;
        }

        for (int i = 0; i < gridX; i++)
        {
            float x = -gridX * 0.5f + 0.5f + i;

            for (int j = 0; j < gridZ; j++)
            {
                float z = -gridZ * 0.5f + 0.5f + j;
                Vector3 worldPosition = CellWorldPosition(cube, x, z);
                cube.CollectNotchFlankingPositions(worldPosition, cubeMergingNeighbors, consumed);
            }
        }
    }

    private static Vector3 CellWorldPosition(CubeVisualBuilder cube, float x, float z)
    {
        Vector3 scale = cube.transform.localScale;
        Quaternion rotation = GetSnappedRotation(cube.transform);
        Vector3 localOffset = new Vector3(scale.x * 0.5f + x, 0f, scale.z * 0.5f + z);
        return cube.transform.position + rotation * localOffset;
    }

    // Same inner-corner detection as PlaceCell, but only used to figure out which flanking cells
    // (one along each of the two walls meeting at the notch) an L-shaped connector will consume.
    private void CollectNotchFlankingPositions(Vector3 worldPosition, List<CubeVisualBuilder> mergingNeighbors, List<Vector3> consumed)
    {
        bool eastOccupied = IsOccupied(worldPosition + new Vector3(1f, 0f, 0f), mergingNeighbors);
        bool westOccupied = IsOccupied(worldPosition + new Vector3(-1f, 0f, 0f), mergingNeighbors);
        bool northOccupied = IsOccupied(worldPosition + new Vector3(0f, 0f, 1f), mergingNeighbors);
        bool southOccupied = IsOccupied(worldPosition + new Vector3(0f, 0f, -1f), mergingNeighbors);

        if (!eastOccupied || !westOccupied || !northOccupied || !southOccupied)
        {
            return;
        }

        bool neNotch = !IsOccupied(worldPosition + new Vector3(1f, 0f, 1f), mergingNeighbors);
        bool nwNotch = !IsOccupied(worldPosition + new Vector3(-1f, 0f, 1f), mergingNeighbors);
        bool seNotch = !IsOccupied(worldPosition + new Vector3(1f, 0f, -1f), mergingNeighbors);
        bool swNotch = !IsOccupied(worldPosition + new Vector3(-1f, 0f, -1f), mergingNeighbors);

        if (!HasInnerCornerPrefabs() || !(neNotch || nwNotch || seNotch || swNotch))
        {
            return;
        }

        ResolveNotchDirection(neNotch, seNotch, nwNotch, out bool notchMaxX, out bool notchMaxZ);

        consumed.Add(worldPosition + new Vector3(0f, 0f, notchMaxZ ? 1f : -1f));
        consumed.Add(worldPosition + new Vector3(notchMaxX ? 1f : -1f, 0f, 0f));
    }

    // Resolves which single diagonal to treat as THE inner corner when multiple are notched at
    // once (e.g. a pinwheel-style merge of 3+ cubes can notch opposite diagonals simultaneously,
    // all 4 cardinals still occupied) - NE takes priority, then SE, then NW, then SW. This ordering
    // must stay identical between CollectNotchFlankingPositions (which flanking cells get reserved)
    // and PlaceCell (which orientation the piece actually spawns as) - previously PlaceCell combined
    // the four notch flags with plain OR instead of this same priority order, so e.g. seNotch and
    // nwNotch both true (with neNotch/swNotch false) made PlaceCell synthesize a NE piece while
    // CollectNotchFlankingPositions had already reserved flanking cells for SE - a piece facing a
    // direction that didn't match what was actually reserved for it.
    private static void ResolveNotchDirection(bool neNotch, bool seNotch, bool nwNotch, out bool notchMaxX, out bool notchMaxZ)
    {
        if (neNotch)
        {
            notchMaxX = true;
            notchMaxZ = true;
        }
        else if (seNotch)
        {
            notchMaxX = true;
            notchMaxZ = false;
        }
        else if (nwNotch)
        {
            notchMaxX = false;
            notchMaxZ = true;
        }
        else
        {
            notchMaxX = false;
            notchMaxZ = false;
        }
    }

    // Builds a transform with the same world position/rotation/scale that neighbor's own
    // visualRoot would have (see EnsureStructure), but parented under this cube's visualRoot
    // instead of the neighbor's own transform.
    private Transform CreateAdoptedVisualRoot(CubeVisualBuilder neighbor, int neighborGridX, int neighborGridZ)
    {
        Transform adopted = new GameObject($"Visual_{neighbor.name}").transform;
        adopted.SetParent(neighbor.transform, worldPositionStays: false);
        adopted.localPosition = new Vector3(0.5f, 0f, 0.5f);
        adopted.localScale = new Vector3(1f / neighborGridX, 1f, 1f / neighborGridZ);
        adopted.SetParent(visualRoot, worldPositionStays: true);
        return adopted;
    }

    // Only the mesh is hidden here - the box collider stays enabled even for an absorbed merging
    // neighbor, so collision for its part of the merged footprint is still there during play.
    private void HideOwnBoxVisuals()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }
    }

    [Button]
    public void ResetCube()
    {
        ResetCube(new HashSet<CubeVisualBuilder>());
    }

    // A merging neighbor's own box mesh gets hidden and its grid gets drawn as children of the
    // host's visualRoot instead of its own (see DrawMergingNeighbors) - so resetting only the
    // clicked cube would destroy that shared visualRoot while leaving every merging neighbor with
    // no box and no generated pieces. Resetting the whole merging cluster together (guarded against
    // revisiting a cube twice, since merging is symmetric and would otherwise recurse forever)
    // keeps them consistent.
    private void ResetCube(HashSet<CubeVisualBuilder> alreadyReset)
    {
        if (!alreadyReset.Add(this))
        {
            return;
        }

        if (colliderRoot != null)
        {
            MoveColliderFromRoot();
            SafeDestroy(colliderRoot.gameObject);
            colliderRoot = null;
        }

        if (visualRoot != null)
        {
            SafeDestroy(visualRoot.gameObject);
            visualRoot = null;
        }

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }

        foreach (CubeVisualBuilder neighbor in FindMergingNeighbors())
        {
            neighbor.ResetCube(alreadyReset);
        }
    }

    private void EnsureStructure()
    {
        gameObject.name = "VisualCube";

        if (colliderRoot == null)
        {
            colliderRoot = new GameObject("CubeCollider").transform;
            colliderRoot.gameObject.layer = gameObject.layer;
            colliderRoot.SetParent(transform, false);
            MoveColliderToRoot();
        }

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }

        if (visualRoot == null)
        {
            visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(transform, false);
            visualRoot.localPosition = new Vector3(0.5f, 0f, 0.5f);
        }
    }

    private void MoveColliderToRoot()
    {
        BoxCollider existing = GetComponent<BoxCollider>();
        BoxCollider moved = colliderRoot.gameObject.AddComponent<BoxCollider>();

        if (existing != null)
        {
            moved.size = existing.size;
            moved.center = existing.center;
            SafeDestroy(existing);
        }
    }

    private void MoveColliderFromRoot()
    {
        BoxCollider existing = colliderRoot.GetComponent<BoxCollider>();
        if (existing == null)
        {
            return;
        }

        BoxCollider restored = gameObject.AddComponent<BoxCollider>();
        restored.size = existing.size;
        restored.center = existing.center;
    }

    private void ClearVisuals()
    {
        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            SafeDestroy(visualRoot.GetChild(i).gameObject);
        }
    }

    private void PlaceGrid(int gridX, int gridZ, Transform targetVisualRoot, List<CubeVisualBuilder> mergingNeighbors, List<CubeVisualBuilder> touchingNeighbors, List<Bounds> claimedBounds, List<Vector3> consumedPositions)
    {
        for (int i = 0; i < gridX; i++)
        {
            float x = -gridX * 0.5f + 0.5f + i;

            for (int j = 0; j < gridZ; j++)
            {
                float z = -gridZ * 0.5f + 0.5f + j;

                Vector3 localPosition = new Vector3(x, 0f, z);
                PlaceCell(localPosition, i, j, gridX, gridZ, targetVisualRoot, mergingNeighbors, touchingNeighbors, claimedBounds, consumedPositions);
            }
        }

        PlaceCenter(gridX, gridZ, targetVisualRoot, claimedBounds);
    }

    // A center piece is just a plain box, so instead of tiling one per interior cell, a single
    // instance is scaled to cover the whole footprint (minus centerScaleMargin, so it sits just
    // inside the edge/corner ring rather than poking past it). Skipped if this cube's own center
    // point is already claimed by an earlier cube in the merge (see DrawMergingNeighbors) - two
    // overlapping cubes would otherwise each draw their own overlapping floor slab there.
    private void PlaceCenter(int gridX, int gridZ, Transform targetVisualRoot, List<Bounds> claimedBounds)
    {
        if (IsClaimedByAnotherCube(targetVisualRoot.position, claimedBounds))
        {
            return;
        }

        GameObject centerPrefab = PickVariant(centerPrefabs);
        SpawnAt(centerPrefab, Vector3.zero, targetVisualRoot, centerYaw, gridX - centerScaleMargin, gridZ - centerScaleMargin);
    }

    // Classifies a cell against the combined footprint of this cube plus any merging neighbors
    // (not just this cube's own grid), so two overlapping cubes read as one merged shape:
    // outer corners/edges where the merged shape's boundary runs straight or turns outward,
    // inner corners where it turns inward (concave, e.g. a T-junction between two cubes). Fully
    // enclosed cells fall through to an individual filler piece if PlaceCenter's single scaled
    // slab doesn't already reach them (see IsCoveredByCenterSlab).
    private void PlaceCell(Vector3 localPosition, int i, int j, int gridX, int gridZ, Transform targetVisualRoot, List<CubeVisualBuilder> mergingNeighbors, List<CubeVisualBuilder> touchingNeighbors, List<Bounds> claimedBounds, List<Vector3> consumedPositions)
    {
        Vector3 worldPosition = targetVisualRoot.TransformPoint(localPosition);

        if (IsClaimedByAnotherCube(worldPosition, claimedBounds) || IsConsumedPosition(worldPosition, consumedPositions))
        {
            return;
        }

        bool eastOccupied = IsOccupied(worldPosition + new Vector3(1f, 0f, 0f), mergingNeighbors);
        bool westOccupied = IsOccupied(worldPosition + new Vector3(-1f, 0f, 0f), mergingNeighbors);
        bool northOccupied = IsOccupied(worldPosition + new Vector3(0f, 0f, 1f), mergingNeighbors);
        bool southOccupied = IsOccupied(worldPosition + new Vector3(0f, 0f, -1f), mergingNeighbors);

        bool maxX = !eastOccupied;
        bool minX = !westOccupied;
        bool maxZ = !northOccupied;
        bool minZ = !southOccupied;

        if ((maxX || minX) && (maxZ || minZ))
        {
            GameObject prefab = PickVariant(outerCornerPrefabs);
            SpawnAt(prefab, localPosition, targetVisualRoot, WorldYawToLocal(targetVisualRoot, CornerYaw(maxX, maxZ) + outerCornerYaw));
            return;
        }

        if (maxX || minX || maxZ || minZ)
        {
            bool onXSide = maxX || minX;

            if (IsEdgeOpenToTouchingNeighbor(worldPosition, onXSide, maxX, maxZ, touchingNeighbors))
            {
                GameObject openFiller = PickVariant(centerPrefabs);
                SpawnAt(openFiller, localPosition, targetVisualRoot, centerYaw);
                return;
            }

            PlaceEdgeRun(localPosition, worldPosition, i, j, gridX, gridZ, onXSide, maxX, maxZ, targetVisualRoot, mergingNeighbors, touchingNeighbors, claimedBounds, consumedPositions);
            return;
        }

        bool neNotch = !IsOccupied(worldPosition + new Vector3(1f, 0f, 1f), mergingNeighbors);
        bool nwNotch = !IsOccupied(worldPosition + new Vector3(-1f, 0f, 1f), mergingNeighbors);
        bool seNotch = !IsOccupied(worldPosition + new Vector3(1f, 0f, -1f), mergingNeighbors);
        bool swNotch = !IsOccupied(worldPosition + new Vector3(-1f, 0f, -1f), mergingNeighbors);

        if (HasInnerCornerPrefabs() && (neNotch || nwNotch || seNotch || swNotch))
        {
            ResolveNotchDirection(neNotch, seNotch, nwNotch, out bool notchMaxX, out bool notchMaxZ);
            GameObject prefab = PickVariant(innerCornerPrefabs);
            SpawnAt(prefab, localPosition, targetVisualRoot, WorldYawToLocal(targetVisualRoot, CornerYaw(notchMaxX, notchMaxZ) + innerCornerYaw));
            return;
        }

        // Fully-enclosed cells are usually covered by PlaceCenter's single scaled slab, but ring
        // cells next to a merge/touch seam sit outside that shrunk slab (it stops short to tuck
        // under a wall that isn't actually there on that side) - give those an individual filler
        // piece instead of leaving a gap.
        if (!IsCoveredByCenterSlab(localPosition, gridX, gridZ))
        {
            GameObject filler = PickVariant(centerPrefabs);
            SpawnAt(filler, localPosition, targetVisualRoot, centerYaw);
        }
    }

    private bool IsCoveredByCenterSlab(Vector3 localPosition, int gridX, int gridZ)
    {
        float halfWidth = (gridX - centerScaleMargin) * 0.5f;
        float halfDepth = (gridZ - centerScaleMargin) * 0.5f;
        return Mathf.Abs(localPosition.x) < halfWidth && Mathf.Abs(localPosition.z) < halfDepth;
    }

    private const int MaxEdgeWidth = 3;

    // A straight run of edge cells can be covered by one piece stretched along its own local Z
    // (the along-the-wall axis for these prefabs, see SpawnAt) instead of a separate 1x1 piece per
    // cell. Looks ahead up to 2 more cells along the row - stopping at this cube's own grid bounds,
    // a claimed/consumed cell, or anything that isn't the same edge type - then randomly picks a
    // width among what fits and marks the extra cells consumed so PlaceGrid's loop skips them.
    //
    // localStepDirection and worldStepDirection are deliberately two different vectors, not one:
    // i/j/gridX/gridZ (and runCenter, a position local to targetVisualRoot) only make sense in the
    // cube's own local grid space, while worldPosition and consumedPositions (world positions used
    // for occupancy checks) only make sense in world space. The two coincide only when the cube's
    // own rotation is identity - once it's rotated, mixing them silently walks/places along the
    // wrong axis.
    private void PlaceEdgeRun(Vector3 localPosition, Vector3 worldPosition, int i, int j, int gridX, int gridZ, bool onXSide, bool maxX, bool maxZ, Transform targetVisualRoot, List<CubeVisualBuilder> mergingNeighbors, List<CubeVisualBuilder> touchingNeighbors, List<Bounds> claimedBounds, List<Vector3> consumedPositions)
    {
        Vector3 localStepDirection = onXSide ? new Vector3(0f, 0f, 1f) : new Vector3(1f, 0f, 0f);
        Vector3 worldStepDirection = GetSnappedRotation(targetVisualRoot) * localStepDirection;
        int remainingInGrid = onXSide ? gridZ - 1 - j : gridX - 1 - i;

        int maxWidth = 1;
        for (int extra = 1; extra <= MaxEdgeWidth - 1 && extra <= remainingInGrid; extra++)
        {
            Vector3 candidate = worldPosition + worldStepDirection * extra;
            if (!IsSameEdgeCell(candidate, mergingNeighbors, touchingNeighbors, claimedBounds, consumedPositions, onXSide, maxX, maxZ))
            {
                break;
            }

            maxWidth = extra + 1;
        }

        int width = Random.Range(1, maxWidth + 1);

        for (int extra = 1; extra < width; extra++)
        {
            consumedPositions.Add(worldPosition + worldStepDirection * extra);
        }

        Vector3 runCenter = localPosition + localStepDirection * ((width - 1) * 0.5f);
        GameObject prefab = PickVariant(edgePrefabs);
        SpawnAt(prefab, runCenter, targetVisualRoot, WorldYawToLocal(targetVisualRoot, EdgeYaw(onXSide, maxX, maxZ) + edgeYaw), footprintZ: width + edgeScaleOverlap);
    }

    // Same classification as PlaceCell's edge check above, run against a cell further along the
    // row to see whether an edge piece can safely stretch to cover it too (same edge type, not a
    // corner, not already claimed or consumed, and not itself opening up to a touching neighbor).
    private bool IsSameEdgeCell(Vector3 candidateWorldPosition, List<CubeVisualBuilder> mergingNeighbors, List<CubeVisualBuilder> touchingNeighbors, List<Bounds> claimedBounds, List<Vector3> consumedPositions, bool onXSide, bool maxX, bool maxZ)
    {
        if (IsClaimedByAnotherCube(candidateWorldPosition, claimedBounds) || IsConsumedPosition(candidateWorldPosition, consumedPositions))
        {
            return false;
        }

        bool eastOccupied = IsOccupied(candidateWorldPosition + new Vector3(1f, 0f, 0f), mergingNeighbors);
        bool westOccupied = IsOccupied(candidateWorldPosition + new Vector3(-1f, 0f, 0f), mergingNeighbors);
        bool northOccupied = IsOccupied(candidateWorldPosition + new Vector3(0f, 0f, 1f), mergingNeighbors);
        bool southOccupied = IsOccupied(candidateWorldPosition + new Vector3(0f, 0f, -1f), mergingNeighbors);

        bool candidateMaxX = !eastOccupied;
        bool candidateMinX = !westOccupied;
        bool candidateMaxZ = !northOccupied;
        bool candidateMinZ = !southOccupied;

        if ((candidateMaxX || candidateMinX) && (candidateMaxZ || candidateMinZ))
        {
            return false;
        }

        bool candidateOnXSide = candidateMaxX || candidateMinX;
        if (candidateOnXSide != onXSide || (onXSide ? candidateMaxX != maxX : candidateMaxZ != maxZ))
        {
            return false;
        }

        return !IsEdgeOpenToTouchingNeighbor(candidateWorldPosition, onXSide, maxX, maxZ, touchingNeighbors);
    }

    private bool HasInnerCornerPrefabs()
    {
        return innerCornerPrefabs != null && innerCornerPrefabs.Count > 0;
    }

    // Two cubes only need to merge if they sit at the same height (same top face, so their
    // floors/ceilings line up) and their footprints actually overlap in space.
    private List<CubeVisualBuilder> FindMergingNeighbors()
    {
        List<CubeVisualBuilder> mergingNeighbors = new List<CubeVisualBuilder>();
        float topY = GetTopY();
        Bounds bounds = GetWorldBounds();

        foreach (CubeVisualBuilder other in FindObjectsByType<CubeVisualBuilder>(FindObjectsSortMode.None))
        {
            if (other == this)
            {
                continue;
            }

            if (Mathf.Approximately(topY, other.GetTopY()) && bounds.Intersects(other.GetWorldBounds()))
            {
                mergingNeighbors.Add(other);
            }
        }

        return mergingNeighbors;
    }

    // Same-height cubes that sit within touchGapTolerance of this one but don't actually overlap
    // (so FindMergingNeighbors doesn't see them as one shape). Cubes that do overlap satisfy this
    // check too, but by the time IsEdgeOpenToTouchingNeighbor runs, that direction is already
    // occupied via the true merge, so it's a no-op for them.
    private List<CubeVisualBuilder> FindTouchingNeighbors()
    {
        List<CubeVisualBuilder> touchingNeighbors = new List<CubeVisualBuilder>();
        float topY = GetTopY();
        Bounds inflatedBounds = InflateXZ(GetWorldBounds(), touchGapTolerance);

        foreach (CubeVisualBuilder other in FindObjectsByType<CubeVisualBuilder>(FindObjectsSortMode.None))
        {
            if (other == this)
            {
                continue;
            }

            if (Mathf.Approximately(topY, other.GetTopY()) && inflatedBounds.Intersects(other.GetWorldBounds()))
            {
                touchingNeighbors.Add(other);
            }
        }

        return touchingNeighbors;
    }

    private static Bounds InflateXZ(Bounds bounds, float amount)
    {
        return new Bounds(bounds.center, bounds.size + new Vector3(amount * 2f, 0f, amount * 2f));
    }

    // Whether a plain edge cell's single open direction actually faces a touching neighbor rather
    // than true empty space - if so it should open into a floor piece instead of a wall (see
    // PlaceCell), since the two cubes read as one continuous space across that small gap.
    private static bool IsEdgeOpenToTouchingNeighbor(Vector3 worldPosition, bool onXSide, bool maxX, bool maxZ, List<CubeVisualBuilder> touchingNeighbors)
    {
        Vector3 direction = onXSide ? new Vector3(maxX ? 1f : -1f, 0f, 0f) : new Vector3(0f, 0f, maxZ ? 1f : -1f);
        Vector3 checkPosition = worldPosition + direction;

        foreach (CubeVisualBuilder neighbor in touchingNeighbors)
        {
            if (Contains2D(neighbor.GetWorldBounds(), checkPosition))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a world-space XZ position falls within this cube's own footprint or any of its
    // merging neighbors' - i.e. whether the combined shape covers that spot.
    private bool IsOccupied(Vector3 worldPosition, List<CubeVisualBuilder> mergingNeighbors)
    {
        if (Contains2D(GetWorldBounds(), worldPosition))
        {
            return true;
        }

        foreach (CubeVisualBuilder other in mergingNeighbors)
        {
            if (Contains2D(other.GetWorldBounds(), worldPosition))
            {
                return true;
            }
        }

        return false;
    }

    // Cells inside a bound already claimed by an earlier cube in the merge (the host, or an
    // earlier neighbor) are skipped, so the overlap region only gets drawn once instead of once
    // per cube that covers it.
    private static bool IsClaimedByAnotherCube(Vector3 worldPosition, List<Bounds> claimedBounds)
    {
        if (claimedBounds == null)
        {
            return false;
        }

        foreach (Bounds bounds in claimedBounds)
        {
            if (Contains2D(bounds, worldPosition))
            {
                return true;
            }
        }

        return false;
    }

    // Cells consumed either by an inner corner's L-shaped connector (see CollectNotchFlankingPositions)
    // or by a wider edge piece that already spans them (see PlaceCell) are skipped so nothing else
    // gets placed on top of that piece's footprint.
    private static bool IsConsumedPosition(Vector3 worldPosition, List<Vector3> consumedPositions)
    {
        if (consumedPositions == null)
        {
            return false;
        }

        const float epsilon = 0.1f;
        foreach (Vector3 consumed in consumedPositions)
        {
            if (Mathf.Abs(worldPosition.x - consumed.x) < epsilon && Mathf.Abs(worldPosition.z - consumed.z) < epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains2D(Bounds bounds, Vector3 worldPosition)
    {
        const float epsilon = 0.1f;
        return worldPosition.x > bounds.min.x + epsilon && worldPosition.x < bounds.max.x - epsilon
            && worldPosition.z > bounds.min.z + epsilon && worldPosition.z < bounds.max.z - epsilon;
    }

    private float GetTopY()
    {
        return GetWorldBounds().max.y;
    }

    // Rotation is expected to be yaw-only in 90-degree steps, but manual placement can leave it a
    // degree or two off a clean cardinal angle (e.g. 179 instead of 180) - every rotation-aware
    // calculation below reads this instead of the raw transform rotation, so that drift doesn't
    // propagate into skewed bounds or slightly-misaligned pieces.
    private static Quaternion GetSnappedRotation(Transform t)
    {
        float snappedYaw = Mathf.Round(t.eulerAngles.y / 90f) * 90f;
        return Quaternion.Euler(0f, snappedYaw, 0f);
    }

    // World-space AABB of this cube, accounting for its own (snapped) yaw rotation - encapsulating
    // just the pivot corner and its diagonal opposite is enough since a 90-degree-multiple rotation
    // always keeps the box axis-aligned in world space, just with X/Z swapped or negated.
    private Bounds GetWorldBounds()
    {
        Quaternion rotation = GetSnappedRotation(transform);
        Vector3 pivotCorner = transform.position;
        Vector3 oppositeCorner = pivotCorner + rotation * transform.localScale;

        Bounds bounds = new Bounds(pivotCorner, Vector3.zero);
        bounds.Encapsulate(oppositeCorner);
        return bounds;
    }

    // Faces outward on the side the cell sits on. 0 deg = North (+Z); Unity's +Y rotation
    // cycles North->East->South->West, so each clockwise quarter-turn is +90.
    private static float EdgeYaw(bool onXSide, bool maxX, bool maxZ)
    {
        if (onXSide)
        {
            return maxX ? 90f : 270f; // East : West
        }

        return maxZ ? 0f : 180f; // North : South
    }

    // Faces outward on the diagonal the cell sits on, same clockwise convention as EdgeYaw.
    private static float CornerYaw(bool maxX, bool maxZ)
    {
        if (maxX)
        {
            return maxZ ? 0f : 90f; // NE : SE
        }

        return maxZ ? 270f : 180f; // NW : SW
    }

    // A prefab's pivot sits at its own min corner (X/Z), same as the cube (see class comment) -
    // so rotating it about that corner in place would swing its footprint into a neighboring
    // cell instead of just changing which side faces outward. Orbiting the corner around the
    // footprint's center by the same yaw keeps it pinned in place for every rotation. footprintX/Z
    // default to 1 (a single cell); PlaceCenter passes the whole grid's size to cover it in one go.
    //
    // yaw here is always local to targetVisualRoot (i.e. rigidly attached to the cube's own
    // rotation, same as footprintX/Z always meaning "along this cube's own local X/Z" regardless of
    // how the cube is rotated in world space) - center/filler pieces pass centerYaw straight in as
    // this local value. Edge/corner pieces instead need to face a specific *world* compass
    // direction (see EdgeYaw/CornerYaw) - those callers convert via WorldYawToLocal before calling
    // this, rather than this method assuming every yaw means the same thing.
    private void SpawnAt(GameObject prefab, Vector3 footprintCenter, Transform targetVisualRoot, float yaw, float footprintX = 1f, float footprintZ = 1f)
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

        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 centerToMinCorner = new Vector3(-footprintX * 0.5f, 0f, -footprintZ * 0.5f);

        instance.transform.SetParent(targetVisualRoot, false);
        instance.transform.localRotation = rotation;
        instance.transform.localScale = new Vector3(footprintX, 1f, footprintZ);
        instance.transform.localPosition = footprintCenter + rotation * centerToMinCorner;
    }

    // EdgeYaw/CornerYaw compute a world-compass facing direction (0/90/180/270), independent of
    // however this cube itself happens to be rotated. SpawnAt's yaw is applied local to
    // targetVisualRoot though, which already carries the cube's own rotation - so a world-facing
    // direction has to be converted to "local yaw relative to this cube's rotation" before being
    // passed in, or the piece ends up facing that direction offset by the cube's own rotation.
    private static float WorldYawToLocal(Transform targetVisualRoot, float worldYaw)
    {
        float parentYaw = GetSnappedRotation(targetVisualRoot).eulerAngles.y;
        return worldYaw - parentYaw;
    }

    private static GameObject PickVariant(List<GameObject> prefabs)
    {
        if (prefabs == null || prefabs.Count == 0)
        {
            return null;
        }

        return prefabs[Random.Range(0, prefabs.Count)];
    }

    private void WarnIfNotWholeUnits(int gridX, int gridZ)
    {
        // Corner/edge pieces are authored 1x1 - a non-integer cube scale would leave a gap or overlap.
        Vector3 scale = transform.localScale;
        if (Mathf.Abs(scale.x - gridX) > 0.01f || Mathf.Abs(scale.z - gridZ) > 0.01f)
        {
            Debug.LogWarning($"[CubeVisualBuilder] Cube X/Z scale ({scale.x}, {scale.z}) isn't a whole number - rounded to {gridX}x{gridZ}.", this);
        }
    }

    private void SafeDestroy(Object obj)
    {
        
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
