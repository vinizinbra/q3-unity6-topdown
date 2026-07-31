using NaughtyAttributes;
using Quantum;
using UnityEngine;

// Bakes every ChunkWaypointMarker under this chunk into Chunk.Waypoints - lets a chunk's AI
// navigation graph be authored as plain points in the Scene view (same idea as
// ChunkCompoundColliderBuilder for wall geometry) instead of hand-typing FPVector3/bitmask
// values in the Inspector. ConnectionMask bit i is set when a straight Unity Physics linecast
// between two markers finds nothing wall-like along the way - see IsPathBlocked. Walls and
// floor share the same "Ground" layer (see EnemyMovementUtility.GetGroundLayerMask's own
// comment) Quantum's own static colliders bake from, so "clear at bake time" matches "clear at
// runtime" as long as a chunk's own geometry never changes shape after LevelGenerationSystem
// places it.
[RequireComponent(typeof(QPrototypeChunk))]
public class ChunkWaypointBaker : MonoBehaviour
{
    // Matches Chunk.Waypoints' fixed array size in Chunk.qtn - keep in sync if that ever changes.
    private const int MaxWaypoints = 16;

    // Ground carries structural walls/floor (see EnemyMovementUtility.GetGroundLayerMask);
    // Obstacle carries solid props that block movement without being level geometry (see
    // EnemyMovementUtility.GetObstacleLayerMask, e.g. GroupSpawnerUtility's own clearance
    // check) - a connection has to be clear of both to actually be walkable.
    public static readonly string[] BlockingLayerNames = { "Ground", "Obstacle" };

    // A hit surface normal within this far off dead-vertical (Vector3.up/down) reads as
    // floor/ceiling, not a wall - see IsPathBlocked. 0.7 is roughly "tilted more than ~45
    // degrees from horizontal," generous enough to still catch a sloped wall while rejecting
    // an actually-flat floor/ceiling hit.
    private const float WallNormalVerticalityThreshold = 0.7f;

    [Button]
    public void BakeWaypoints()
    {
        ChunkWaypointMarker[] markers = GetComponentsInChildren<ChunkWaypointMarker>();

        if (markers.Length == 0)
        {
            Debug.LogWarning($"[ChunkWaypointBaker] No ChunkWaypointMarker found under {name} - leaving Waypoints untouched.", this);
            return;
        }

        if (markers.Length > MaxWaypoints)
        {
            Debug.LogError($"[ChunkWaypointBaker] {name} has {markers.Length} markers, only {MaxWaypoints} fit in Chunk.Waypoints - remove {markers.Length - MaxWaypoints}.", this);
            return;
        }

        int wallMask = UnityEngine.LayerMask.GetMask(BlockingLayerNames);
        var waypoints = new Quantum.Prototypes.WaypointNodePrototype[MaxWaypoints];

        for (int i = 0; i < MaxWaypoints; i++)
        {
            waypoints[i] = new Quantum.Prototypes.WaypointNodePrototype();
        }

        int connectionCount = 0;

        for (int i = 0; i < markers.Length; i++)
        {
            Vector3 worldPositionI = markers[i].transform.position;
            uint mask = 0;

            for (int j = 0; j < markers.Length; j++)
            {
                if (i == j)
                    continue;

                Vector3 worldPositionJ = markers[j].transform.position;

                if (IsPathBlocked(worldPositionI, worldPositionJ, wallMask) == false)
                {
                    mask |= 1u << j;
                    connectionCount++;
                }
            }

            waypoints[i].LocalPosition = transform.InverseTransformPoint(worldPositionI).ToFPVector3();
            waypoints[i].ConnectionMask = mask;
        }

        QPrototypeChunk chunkPrototype = GetComponent<QPrototypeChunk>();
        chunkPrototype.Prototype.Waypoints = waypoints;
        chunkPrototype.Prototype.WaypointCount = (byte)markers.Length;

        Debug.Log($"[ChunkWaypointBaker] Baked {markers.Length} waypoint(s), {connectionCount / 2} connection(s), on {name}.", this);
    }

    // RaycastAll instead of Linecast - a single closest hit isn't enough to tell "blocked by a
    // wall" apart from "grazed the floor/ceiling on the way to an actual wall behind it," so
    // every hit along the segment gets checked and only a wall-like one (see
    // WallNormalVerticalityThreshold) counts as blocking.
    public static bool IsPathBlocked(Vector3 from, Vector3 to, int layerMask)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance <= 0f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(from, delta / distance, distance, layerMask, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (Mathf.Abs(hit.normal.y) < WallNormalVerticalityThreshold)
                return true;
        }

        return false;
    }
}
