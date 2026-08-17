using UnityEngine;

// Marker for a single box in a chunk's compound collider - ChunkCompoundColliderBuilder collects
// every one of these under the same chunk root and bakes it into one Quantum PhysicsCollider.
// Gizmo only draws on selection now (either this cube directly, or the whole set at once via
// ChunkCompoundColliderBuilder.OnDrawGizmosSelected) - drawing unconditionally for every cube in
// every loaded chunk washed the Scene view out in solid orange on anything but a tiny level.
[RequireComponent(typeof(BoxCollider))]
public class ChunkWallCube : MonoBehaviour
{
    private void OnDrawGizmosSelected()
    {
        DrawGizmo();
    }

    public void DrawGizmo()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
    }
}
