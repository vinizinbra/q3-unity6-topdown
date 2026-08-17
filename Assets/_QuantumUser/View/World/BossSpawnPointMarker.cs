using UnityEngine;

// Marker for one of the Boss chunk's optional hand-authored boss spawn points -
// BossArenaMarkerBaker collects every instance of this under the same chunk root (up to
// BossArena.SpawnPoints' own [4] cap) and writes them into BossArena.SpawnPoints/
// SpawnPointCount. Place more than one to spawn multiple copies of the same
// SurvivalPhase.BossPrototype (e.g. twin bosses) - see RunPhaseUtility.SpawnBoss. Same idea as
// ChunkRespawnPointMarker/BossTeleportPointMarker, just its own gizmo color so all three marker
// kinds read apart in the Scene view.
public class BossSpawnPointMarker : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
}
