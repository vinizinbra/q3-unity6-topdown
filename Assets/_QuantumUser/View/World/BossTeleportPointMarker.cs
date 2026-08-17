using UnityEngine;

// Marker for one of the Boss chunk's optional hand-authored player-teleport points -
// BossArenaMarkerBaker collects every instance of this under the same chunk root (up to
// BossArena.TeleportPoints' own [4] cap - one per player slot, so connected players land spread
// out instead of stacked on the same spot) and writes them into BossArena.TeleportPoints/
// TeleportPointCount. Same idea as ChunkRespawnPointMarker/BossSpawnPointMarker, just its own
// gizmo color so all three marker kinds read apart in the Scene view.
public class BossTeleportPointMarker : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
}
