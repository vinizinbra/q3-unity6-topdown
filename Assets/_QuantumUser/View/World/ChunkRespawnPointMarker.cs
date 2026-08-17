using UnityEngine;

// Marker for a chunk's optional hand-authored fall-respawn point - ChunkRespawnPointBaker
// collects the single instance of this under the same chunk root and writes it into
// Chunk.RespawnPoint/HasRespawnPoint. Same idea as ChunkWaypointMarker, just a different gizmo
// color so the two marker kinds read apart in the Scene view.
public class ChunkRespawnPointMarker : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
}
