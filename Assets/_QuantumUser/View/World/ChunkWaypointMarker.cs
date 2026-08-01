using UnityEngine;

// Marker for a single node in a chunk's baked AI navigation graph - ChunkWaypointBaker collects
// every one of these under the same chunk root, in child order, and writes them into
// Chunk.Waypoints. Connection lines are drawn by the baker instead of here (see
// ChunkWaypointBaker.OnDrawGizmos) - it re-checks live every frame rather than replaying whatever
// ConnectionMask was baked last, so the graph shown in the Scene view never goes stale as markers
// get moved around.
public class ChunkWaypointMarker : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
}
