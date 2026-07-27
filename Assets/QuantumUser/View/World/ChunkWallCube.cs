using UnityEngine;

// Marker for a single box in a chunk's compound collider - ChunkCompoundColliderBuilder collects
// every one of these under the same chunk root and bakes it into one Quantum PhysicsCollider.
[RequireComponent(typeof(BoxCollider))]
public class ChunkWallCube : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
    }
}
