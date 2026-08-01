using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Bakes every ChunkWaypointMarker under this chunk into Chunk.Waypoints - lets a chunk's AI
// navigation graph be authored as plain points in the Scene view (same idea as
// ChunkCompoundColliderBuilder for wall geometry) instead of hand-typing FPVector3/bitmask
// values in the Inspector. Baking first snaps every marker onto the floor beneath it (see
// SnapToGround), then connects two markers (ConnectionMask bit i) when a straight linecast
// between their snapped positions hits nothing on the Ground/Obstacle layers - see
// IsPathBlocked. Walls and floor share the same "Ground" layer (see
// EnemyMovementUtility.GetGroundLayerMask's own comment) Quantum's own static colliders bake
// from, so "clear at bake time" matches "clear at runtime" as long as a chunk's own geometry
// never changes shape after LevelGenerationSystem places it.
//
// MUST be run on an instance placed in an open Scene, not on the prefab asset in isolated Prefab
// Mode - confirmed on this project that Prefab Mode's isolated stage does not reliably surface its
// own colliders to UnityEngine.Physics queries (Raycast/Linecast/RaycastAll all find nothing, even
// with every layer + triggers included, even after adding a Rigidbody to force a physics resync),
// while the exact same hierarchy placed in a Scene works fine. Bake on a Scene instance, then
// Prefab -> Apply to Prefab to push the result (including SnapToGround's repositioned markers)
// back into the asset. Prefab Mode is still fine for arranging markers by eye - just don't trust
// BakeWaypoints/TestConnection/the gizmo while inside it.
[RequireComponent(typeof(QPrototypeChunk))]
public class ChunkWaypointBaker : MonoBehaviour
{
    // Matches Chunk.Waypoints' fixed array size in Chunk.qtn - keep in sync if that ever changes.
    private const int MaxWaypoints = 16;

    // Ground carries structural walls/floor (see EnemyMovementUtility.GetGroundLayerMask);
    // Obstacle carries solid props that block movement without being level geometry (see
    // EnemyMovementUtility.GetObstacleLayerMask, e.g. GroupSpawnerUtility's own clearance
    // check) - a connection has to be clear of both to actually be walkable.
    public UnityEngine.LayerMask BlockingLayerNames;

    // Markers are snapped to sit this far above the floor they land on, not exactly on it - see
    // SnapToGround. This also keeps the connection linecast (see IsPathBlocked) clear of the
    // floor collider itself: two markers snapped to the same offset are lifted off the floor
    // plane rather than grazing along its top surface.
    private const float GroundOffset = 0.2f;

    [Button]
    public void BakeWaypoints()
    {
        ChunkWaypointMarker[] markers = GetComponentsInChildren<ChunkWaypointMarker>();

        if (markers.Length == 0)
        {
            LogHelper.Warn("ChunkWaypointBaker", $"No ChunkWaypointMarker found under {name} - leaving Waypoints untouched.", this);
            return;
        }

        if (markers.Length > MaxWaypoints)
        {
            LogHelper.Error("ChunkWaypointBaker", $"{name} has {markers.Length} markers, only {MaxWaypoints} fit in Chunk.Waypoints - remove {markers.Length - MaxWaypoints}.", this);
            return;
        }

        foreach (ChunkWaypointMarker marker in markers)
        {
            SnapToGround(marker);
        }

        var waypoints = new Quantum.Prototypes.WaypointNodePrototype[MaxWaypoints];

        for (int i = 0; i < MaxWaypoints; i++)
        {
            waypoints[i] = new Quantum.Prototypes.WaypointNodePrototype();
        }

        int connectionCount = 0;

        for (int i = 0; i < markers.Length; i++)
        {
            waypoints[i].LocalPosition = transform.InverseTransformPoint(markers[i].transform.position).ToFPVector3();
        }

        // Blocked-ness is symmetric - a clear linecast from i to j is just as clear from j to i -
        // so each pair only needs checking once, not twice.
        for (int i = 0; i < markers.Length; i++)
        {
            Vector3 worldPositionI = markers[i].transform.position;

            for (int j = i + 1; j < markers.Length; j++)
            {
                Vector3 worldPositionJ = markers[j].transform.position;

                if (IsPathBlocked(worldPositionI, worldPositionJ, BlockingLayerNames) == false)
                {
                    waypoints[i].ConnectionMask |= 1u << j;
                    waypoints[j].ConnectionMask |= 1u << i;
                    connectionCount++;
                }
            }
        }

        QPrototypeChunk chunkPrototype = GetComponent<QPrototypeChunk>();
        chunkPrototype.Prototype.Waypoints = waypoints;
        chunkPrototype.Prototype.WaypointCount = (byte)markers.Length;

        LogHelper.Log("ChunkWaypointBaker", $"Baked {markers.Length} waypoint(s), {connectionCount} clear connection(s) on {name}.", this);
    }

    // Casts straight down from the marker's authored position to find the floor beneath it,
    // then repositions the marker GroundOffset above that hit point - so every baked waypoint
    // actually sits on the chunk's geometry regardless of where it was eyeballed in the Scene
    // view.
    private static void SnapToGround(ChunkWaypointMarker marker)
    {
        if (Physics.Raycast(marker.transform.position, Vector3.down, out RaycastHit hit) == false)
        {
            LogHelper.Warn("ChunkWaypointBaker", $"{marker.name}: no ground found below {marker.transform.position} - leaving position untouched.", marker);
            return;
        }

        marker.transform.position = hit.point + Vector3.up * GroundOffset;
    }

    // A straight linecast between two (already ground-snapped) markers - any hit at all on the
    // Ground/Obstacle layers counts as blocked, no connection.
    public static bool IsPathBlocked(Vector3 from, Vector3 to, int layerMask)
    {
        return Physics.Linecast(from, to, layerMask, QueryTriggerInteraction.Ignore);
    }

    [BoxGroup("Test Connection")]
    public Transform TestPointA;

    [BoxGroup("Test Connection")]
    public Transform TestPointB;

    // Standalone sanity check for exactly one pair, independent of any ChunkWaypointMarker setup -
    // assign any two Transforms (existing markers or scratch empties) and this reports not just
    // blocked/clear but what it actually hit, so "why is this pair connecting when it shouldn't"
    // is answerable without re-running a full bake and staring at the gizmo guessing.
    // Every layer, triggers included, regardless of BlockingLayerNames or the project's global
    // Physics.queriesHitTriggers setting - this is a "does Unity's physics see ANYTHING here at
    // all" sanity check, not the real connection test (IsPathBlocked, restricted to
    // BlockingLayerNames, is what BakeWaypoints actually uses). If this comes back with zero hits,
    // it's not a layer-mask problem - the collider isn't in the physics world from here at all.
    [Button]
    public void TestConnection()
    {
        if (TestPointA == null || TestPointB == null)
        {
            LogHelper.Warn("ChunkWaypointBaker", "Assign both TestPointA and TestPointB before testing.", this);
            return;
        }

        Vector3 from = TestPointA.position;
        Vector3 to = TestPointB.position;
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance <= 0f)
        {
            LogHelper.Warn("ChunkWaypointBaker", "TestPointA and TestPointB are at the same position.", this);
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Collide);

        if (hits.Length == 0)
        {
            LogHelper.Log("ChunkWaypointBaker", $"TEST {TestPointA.name} -> {TestPointB.name}: Physics found NOTHING over {distance:F2}m, on ANY layer, triggers included. " +
                      "Not a layer-mask problem - the collider isn't in the physics world from here. Check: the GameObject (and every parent) is active, " +
                      "the Collider component itself is enabled, and if you're in an isolated Prefab Mode stage, the collider actually belongs to THIS prefab " +
                      "(a collider sitting in the open Scene, or on a different prefab, isn't visible from inside another prefab's isolated stage).", this);
            return;
        }

        int blockingMask = BlockingLayerNames;

        foreach (RaycastHit hit in hits)
        {
            bool isBlocking = (blockingMask & (1 << hit.collider.gameObject.layer)) != 0;
            LogHelper.Log("ChunkWaypointBaker", $"TEST {TestPointA.name} -> {TestPointB.name}: hit '{hit.collider.name}' " +
                      $"(layer {UnityEngine.LayerMask.LayerToName(hit.collider.gameObject.layer)}, IsTrigger={hit.collider.isTrigger}) " +
                      $"at {hit.distance:F2}m of {distance:F2}m - {(isBlocking ? "COUNTS as blocking (its layer is in BlockingLayerNames)" : "does NOT count as blocking (its layer is NOT in BlockingLayerNames)")}.", this);
        }
    }

    // Live preview of exactly what BakeWaypoints would connect right now - re-runs IsPathBlocked
    // every frame instead of replaying stale baked data, so dragging a marker or an obstacle
    // around updates the graph in the Scene view immediately, with no bake needed just to check
    // it. Green = will connect, red = blocked (drawn too, not just skipped, so a connection you
    // expected but don't see reads as "blocked" instead of looking identical to "never checked").
    private void OnDrawGizmosSelected()
    {
        ChunkWaypointMarker[] markers = GetComponentsInChildren<ChunkWaypointMarker>();

        if (markers.Length < 2)
            return;

        for (int i = 0; i < markers.Length; i++)
        {
            Vector3 worldPositionI = markers[i].transform.position;

            for (int j = i + 1; j < markers.Length; j++)
            {
                Vector3 worldPositionJ = markers[j].transform.position;
                bool blocked = IsPathBlocked(worldPositionI, worldPositionJ, BlockingLayerNames);

                Gizmos.color = blocked == true ? Color.red : Color.green;
                Gizmos.DrawLine(worldPositionI, worldPositionJ);
            }
        }
    }
}
