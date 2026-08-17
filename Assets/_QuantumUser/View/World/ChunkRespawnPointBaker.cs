using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Bakes this chunk's single optional ChunkRespawnPointMarker into Chunk.RespawnPoint/
// HasRespawnPoint - see PlayerFallSystem.ResolveRespawnPosition, which prefers this baked point
// over its own automatic edge-inset fallback whenever one is authored (typically on Traversal
// chunks, whose open drops are often in the chunk's own interior, not just its outer boundary).
// Baking snaps the marker onto the floor beneath it first (see SnapToGround), same idea as
// ChunkWaypointBaker.
//
// MUST be run on an instance placed in an open Scene, not on the prefab asset in isolated Prefab
// Mode - see ChunkWaypointBaker's own comment, confirmed on this project that Prefab Mode's
// isolated stage does not reliably surface its own colliders to UnityEngine.Physics queries. Bake
// on a Scene instance, then Prefab -> Apply to Prefab to push the result back into the asset.
[RequireComponent(typeof(QPrototypeChunk))]
public class ChunkRespawnPointBaker : MonoBehaviour
{
    // Markers are snapped to sit this far above the floor they land on, not exactly on it - see
    // SnapToGround. Matches ChunkWaypointBaker's own GroundOffset.
    private const float GroundOffset = 0.2f;

    [Button]
    public void BakeRespawnPoint()
    {
        ChunkRespawnPointMarker[] markers = GetComponentsInChildren<ChunkRespawnPointMarker>();
        QPrototypeChunk chunkPrototype = GetComponent<QPrototypeChunk>();

        if (markers.Length == 0)
        {
            chunkPrototype.Prototype.HasRespawnPoint = false;
            LogHelper.Warn("ChunkRespawnPointBaker", $"No ChunkRespawnPointMarker found under {name} - HasRespawnPoint cleared, PlayerFallSystem will fall back to its automatic edge-inset respawn here.", this);
            return;
        }

        if (markers.Length > 1)
        {
            LogHelper.Error("ChunkRespawnPointBaker", $"{name} has {markers.Length} ChunkRespawnPointMarker instances, only 1 is allowed per chunk - remove {markers.Length - 1}.", this);
            return;
        }

        ChunkRespawnPointMarker marker = markers[0];
        SnapToGround(marker);

        chunkPrototype.Prototype.RespawnPoint = transform.InverseTransformPoint(marker.transform.position).ToFPVector3();
        chunkPrototype.Prototype.HasRespawnPoint = true;

        LogHelper.Log("ChunkRespawnPointBaker", $"Baked respawn point on {name} at local {chunkPrototype.Prototype.RespawnPoint}.", this);
    }

    // Casts straight down from the marker's authored position to find the floor beneath it, then
    // repositions the marker GroundOffset above that hit point - same as ChunkWaypointBaker's own
    // SnapToGround.
    private static void SnapToGround(ChunkRespawnPointMarker marker)
    {
        if (Physics.Raycast(marker.transform.position, Vector3.down, out RaycastHit hit) == false)
        {
            LogHelper.Warn("ChunkRespawnPointBaker", $"{marker.name}: no ground found below {marker.transform.position} - leaving position untouched.", marker);
            return;
        }

        marker.transform.position = hit.point + Vector3.up * GroundOffset;
    }
}
