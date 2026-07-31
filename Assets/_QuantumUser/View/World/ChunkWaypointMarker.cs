using System;
using Quantum;
using UnityEngine;

// Marker for a single node in a chunk's baked AI navigation graph - ChunkWaypointBaker collects
// every one of these under the same chunk root, in child order, and writes them into
// Chunk.Waypoints. Also re-draws whatever ConnectionMask was baked last, straight off the
// sibling QPrototypeChunk, so the graph stays visible in the Scene view without re-running the
// bake just to check it.
public class ChunkWaypointMarker : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.3f);

        DrawConnections();
    }

    private void DrawConnections()
    {
        QPrototypeChunk chunkPrototype = GetComponentInParent<QPrototypeChunk>();

        if (chunkPrototype == null || chunkPrototype.Prototype.Waypoints == null)
            return;

        ChunkWaypointMarker[] siblings = chunkPrototype.GetComponentsInChildren<ChunkWaypointMarker>();
        int index = Array.IndexOf(siblings, this);

        if (index < 0 || index >= chunkPrototype.Prototype.Waypoints.Length)
            return;

        uint mask = chunkPrototype.Prototype.Waypoints[index].ConnectionMask;
        Gizmos.color = Color.green;

        for (int i = 0; i < siblings.Length && i < 32; i++)
        {
            if (i == index || (mask & (1u << i)) == 0)
                continue;

            Gizmos.DrawLine(transform.position, siblings[i].transform.position);
        }
    }
}
