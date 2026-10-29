using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// Where a water prop (a TilesetDefinition.waterScatter model - e.g. a Carnival ride on its island, a coaster's pylon
// footings) touches the water, as plan discs in the prefab's local space: (x, radius, z) - the ShoreObstacles format.
// TilesetPlatformBuilder.ScatterWater registers them (transformed by the spawn's yaw / scale) so the baked shore field
// rings foam around them. Pre-computed in the Editor ("Compute From Meshes"): imported meshes aren't readable in builds.
public class WaterFootprint : MonoBehaviour
{
    [SerializeField, Tooltip("(local x, radius, local z) per disc.")]
    private Vector3[] discs = new Vector3[0];

    [SerializeField, Tooltip("Compute: vertices at or below this local height count as touching the water line.")]
    private float waterlineMaxY = 0.35f;

    [SerializeField, Tooltip("Compute: grid used to group the touching vertices into separate discs (pylons, islands).")]
    private float clusterCell = 0.6f;

    public IReadOnlyList<Vector3> Discs => discs;

#if UNITY_EDITOR
    [Button("Compute From Meshes")]
    public void ComputeFromMeshes()
    {
        var cells = new Dictionary<Vector2Int, List<Vector2>>();
        var toRoot = transform.worldToLocalMatrix;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null)
                continue;
            var m = toRoot * mf.transform.localToWorldMatrix;
            var verts = mf.sharedMesh.vertices;
            var tris = mf.sharedMesh.triangles;
            void Add(Vector3 p)
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x / clusterCell), Mathf.FloorToInt(p.z / clusterCell));
                if (!cells.TryGetValue(c, out var list))
                    cells[c] = list = new List<Vector2>();
                list.Add(new Vector2(p.x, p.z));
            }
            // sample along every triangle edge that stays at / under the water line (low-poly islands have few,
            // far-apart vertices - their rims would otherwise fall into separate clusters)
            for (var i = 0; i < tris.Length; i += 3)
            for (var e = 0; e < 3; e++)
            {
                var a = m.MultiplyPoint3x4(verts[tris[i + e]]);
                var b = m.MultiplyPoint3x4(verts[tris[i + (e + 1) % 3]]);
                if (a.y > waterlineMaxY || b.y > waterlineMaxY)
                    continue;
                var steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / (clusterCell * 0.5f)));
                for (var k = 0; k <= steps; k++)
                    Add(Vector3.Lerp(a, b, k / (float)steps));
            }
        }

        // flood-fill neighbouring cells into clusters; each cluster -> its centroid + the farthest point (+ a margin)
        var result = new List<Vector3>();
        var seen = new HashSet<Vector2Int>();
        foreach (var start in cells.Keys)
        {
            if (!seen.Add(start))
                continue;
            var pts = new List<Vector2>();
            var stack = new Stack<Vector2Int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                pts.AddRange(cells[c]);
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    var n = new Vector2Int(c.x + dx, c.y + dz);
                    if (cells.ContainsKey(n) && seen.Add(n))
                        stack.Push(n);
                }
            }

            var centre = Vector2.zero;
            foreach (var p in pts)
                centre += p;
            centre /= pts.Count;
            var r = 0f;
            foreach (var p in pts)
                r = Mathf.Max(r, (p - centre).magnitude);
            result.Add(new Vector3(centre.x, r + 0.05f, centre.y));
        }
        discs = result.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
