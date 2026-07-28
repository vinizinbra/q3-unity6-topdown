using System.Collections.Generic;
using FluffyUnderware.Curvy.ThirdParty.LibTessDotNet;
using NaughtyAttributes;
using UnityEngine;

// Same authoring convention as CubeVisualBuilder: pivot on one of the box's own vertices. Unlike
// CubeVisualBuilder's blocky wall grid though, overlapping lake cubes merge into one smooth flat
// mesh - only XZ matters (the lake sits flat at this cube's own Y). The lake is built as two
// submeshes sharing one mesh: an inner "water body" (each cube footprint shrunk inward by
// foamBorderWidth, then unioned+rounded) and an outer "foam ring" - the full-size union's outline
// minus that same inner outline, which LibTessDotNet computes natively as a hole via opposite
// winding. Every corner (outer AND inner/concave notches where two cubes join) is filleted by the
// same corner-rounding math, since it only depends on the angle between two edges, not which way
// the corner turns.
public class LakeVisualBuilder : MonoBehaviour
{
    [SerializeField] private Material waterMaterial;
    [SerializeField] private Material foamMaterial;

    [SerializeField, Tooltip("Small lift above this cube's own Y, to avoid z-fighting with the ground.")]
    private float waterSurfaceOffset = 0.02f;

    [SerializeField, Tooltip("Fillet radius applied to every corner of the merged outline - both outer corners and the inner/concave notches where two cubes join.")]
    private float cornerRadius = 1f;

    [SerializeField, Tooltip("Arc segments per rounded corner - higher is smoother.")]
    private int cornerSegments = 6;

    [SerializeField, Tooltip("World-space width of the foam ring running along the shoreline.")]
    private float foamBorderWidth = 1f;

    [SerializeField, HideInInspector] private MeshFilter meshFilter;
    [SerializeField, HideInInspector] private MeshRenderer meshRenderer;

    // Captured once, before Generate() ever overwrites meshFilter.sharedMesh with the built lake
    // mesh - ResetLake() puts this back so the cube shows its original placeholder box again, and
    // GetWorldFootprint() keeps measuring the original box instead of the generated lake mesh on a
    // second Generate() call.
    [SerializeField, HideInInspector] private Mesh placeholderMesh;
    [SerializeField, HideInInspector] private Bounds placeholderLocalBounds;
    [SerializeField, HideInInspector] private bool placeholderCaptured;

    // Same reasoning as CubeVisualBuilder.consumedByMerge - only the cube whose Start() runs first
    // builds the whole cluster's mesh; every other member in the cluster just gets its own
    // placeholder box hidden, so the merged lake doesn't get drawn once per cube.
    private static readonly HashSet<LakeVisualBuilder> consumedByMerge = new HashSet<LakeVisualBuilder>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetGenerationState()
    {
        consumedByMerge.Clear();
    }

    public void Start()
    {
        if (consumedByMerge.Contains(this))
        {
            return;
        }

        Generate();
    }

    [Button]
    public void Generate()
    {
        CapturePlaceholder();

        List<LakeVisualBuilder> cluster = FindMergeCluster();

        consumedByMerge.Add(this);
        foreach (LakeVisualBuilder member in cluster)
        {
            consumedByMerge.Add(member);
            member.HideOwnVisuals();
        }

        var footprints = new List<Rect> { GetWorldFootprint() };
        foreach (LakeVisualBuilder member in cluster)
        {
            footprints.Add(member.GetWorldFootprint());
        }

        List<List<Vector2>> outerLoops = UnionAndRound(footprints, cornerRadius);

        var innerFootprints = new List<Rect>();
        foreach (Rect footprint in footprints)
        {
            Rect shrunk = Shrink(footprint, foamBorderWidth);
            if (shrunk.width > 0f && shrunk.height > 0f)
            {
                innerFootprints.Add(shrunk);
            }
        }

        // No inner footprint survives shrinking (the lake is narrower than the foam border
        // everywhere) - the whole shape is just foam, no open water.
        float innerRadius = Mathf.Max(cornerRadius - foamBorderWidth, 0.01f);
        List<List<Vector2>> innerLoops = innerFootprints.Count > 0 ? UnionAndRound(innerFootprints, innerRadius) : new List<List<Vector2>>();

        var waterTess = new Tess();
        foreach (List<Vector2> loop in innerLoops)
        {
            waterTess.AddContour(ToContourVertices(loop), ContourOrientation.CounterClockwise);
        }
        waterTess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);

        // The ring is "outer minus inner" - feeding the inner loops back in with reversed winding
        // makes LibTessDotNet treat them as a hole under WindingRule.NonZero, the standard
        // technique for tessellating a shape with a hole.
        var foamTess = new Tess();
        foreach (List<Vector2> loop in outerLoops)
        {
            foamTess.AddContour(ToContourVertices(loop), ContourOrientation.CounterClockwise);
        }
        foreach (List<Vector2> loop in innerLoops)
        {
            foamTess.AddContour(ToContourVertices(loop), ContourOrientation.Clockwise);
        }
        foamTess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);

        EnsureStructure();
        meshFilter.sharedMesh = CombineSubmeshes(BuildSubmeshGeometry(waterTess), BuildSubmeshGeometry(foamTess));
        meshRenderer.sharedMaterials = new[] { waterMaterial, foamMaterial };
    }

    // Undoes Generate() across this cube's current merge cluster: puts every hidden neighbor's
    // placeholder box visual back, restores this cube's own placeholder mesh, and forgets the
    // cluster so Start()/Generate() will rebuild from scratch next time.
    [Button]
    public void ResetLake()
    {
        List<LakeVisualBuilder> cluster = FindMergeCluster();

        RestoreOwnVisuals();
        consumedByMerge.Remove(this);

        foreach (LakeVisualBuilder member in cluster)
        {
            member.RestoreOwnVisuals();
            consumedByMerge.Remove(member);
        }
    }

    private void RestoreOwnVisuals()
    {
        MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
        if (ownRenderer != null)
        {
            ownRenderer.enabled = true;
        }

        if (meshFilter != null && placeholderMesh != null)
        {
            meshFilter.sharedMesh = placeholderMesh;
        }
    }

    // Every cube transitively connected to this one through overlapping footprints (A overlaps B,
    // B overlaps C, even if A and C don't directly overlap) - same BFS as CubeVisualBuilder's own
    // FindMergeCluster.
    private List<LakeVisualBuilder> FindMergeCluster()
    {
        var cluster = new List<LakeVisualBuilder>();
        var visited = new HashSet<LakeVisualBuilder> { this };
        var frontier = new Queue<LakeVisualBuilder>();
        frontier.Enqueue(this);

        while (frontier.Count > 0)
        {
            foreach (LakeVisualBuilder neighbor in frontier.Dequeue().FindOverlappingNeighbors())
            {
                if (visited.Add(neighbor))
                {
                    cluster.Add(neighbor);
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return cluster;
    }

    private List<LakeVisualBuilder> FindOverlappingNeighbors()
    {
        var neighbors = new List<LakeVisualBuilder>();
        Rect footprint = GetWorldFootprint();

        foreach (LakeVisualBuilder other in FindObjectsByType<LakeVisualBuilder>(FindObjectsSortMode.None))
        {
            if (other != this && footprint.Overlaps(other.GetWorldFootprint()))
            {
                neighbors.Add(other);
            }
        }

        return neighbors;
    }

    private static Rect Shrink(Rect rect, float amount)
    {
        return Rect.MinMaxRect(rect.xMin + amount, rect.yMin + amount, rect.xMax - amount, rect.yMax - amount);
    }

    // Unions a set of rectangular footprints into their sharp-cornered outline (BoundaryContours),
    // then fillets every corner of that outline.
    private static List<List<Vector2>> UnionAndRound(List<Rect> footprints, float radius)
    {
        var unionTess = new Tess();
        foreach (Rect footprint in footprints)
        {
            AddRectContour(footprint, unionTess);
        }
        unionTess.Tessellate(WindingRule.Positive, ElementType.BoundaryContours, 3);

        var rounded = new List<List<Vector2>>();
        foreach (List<Vector2> loop in ExtractBoundaryLoops(unionTess))
        {
            rounded.Add(RoundContour(loop, radius, 6));
        }

        return rounded;
    }

    // Wound counter-clockwise (as plotted in the X/Z plane) so WindingRule.Positive treats each
    // rectangle's interior as +1 - overlapping rectangles then just add on top of each other and
    // still resolve to one filled region instead of a self-intersecting mess.
    private static void AddRectContour(Rect rect, Tess tess)
    {
        var contour = new ContourVertex[4];
        contour[0].Position = new Vec3 { X = rect.xMin, Y = rect.yMin };
        contour[1].Position = new Vec3 { X = rect.xMax, Y = rect.yMin };
        contour[2].Position = new Vec3 { X = rect.xMax, Y = rect.yMax };
        contour[3].Position = new Vec3 { X = rect.xMin, Y = rect.yMax };

        tess.AddContour(contour);
    }

    // BoundaryContours mode packs Elements as (startVertex, vertexCount) pairs instead of triangle
    // indices - each pair is one closed loop read straight out of Vertices.
    private static List<List<Vector2>> ExtractBoundaryLoops(Tess tess)
    {
        var loops = new List<List<Vector2>>();

        for (int i = 0; i < tess.ElementCount; i++)
        {
            int start = tess.Elements[i * 2];
            int count = tess.Elements[i * 2 + 1];

            var loop = new List<Vector2>(count);
            for (int j = 0; j < count; j++)
            {
                Vec3 position = tess.Vertices[start + j].Position;
                loop.Add(new Vector2(position.X, position.Y));
            }

            loops.Add(loop);
        }

        return loops;
    }

    private static ContourVertex[] ToContourVertices(List<Vector2> loop)
    {
        var contour = new ContourVertex[loop.Count];
        for (int i = 0; i < loop.Count; i++)
        {
            contour[i].Position = new Vec3 { X = loop[i].x, Y = loop[i].y };
        }

        return contour;
    }

    // Replaces every corner of a closed polygon loop with a tangent circular arc of the given
    // radius - works identically for outer (convex) and inner (concave) corners since the fillet
    // geometry only depends on the angle between the two edges meeting at the corner, not which
    // way the corner turns. Standard fillet trig: for a corner with interior angle phi (between
    // the two edge directions pointing away from the corner), the tangent points sit
    // radius/tan(phi/2) back along each edge, and the arc center sits radius/sin(phi/2) from the
    // corner along the angle bisector.
    private static List<Vector2> RoundContour(List<Vector2> points, float radius, int segments)
    {
        int count = points.Count;
        if (radius <= 0f || segments < 1 || count < 3)
        {
            return points;
        }

        var rounded = new List<Vector2>(count * (segments + 1));

        for (int i = 0; i < count; i++)
        {
            Vector2 prev = points[(i - 1 + count) % count];
            Vector2 corner = points[i];
            Vector2 next = points[(i + 1) % count];

            Vector2 toPrev = prev - corner;
            Vector2 toNext = next - corner;
            float prevLen = toPrev.magnitude;
            float nextLen = toNext.magnitude;

            if (prevLen < 1e-4f || nextLen < 1e-4f)
            {
                rounded.Add(corner);
                continue;
            }

            Vector2 dirPrev = toPrev / prevLen;
            Vector2 dirNext = toNext / nextLen;

            float angleDeg = Vector2.Angle(dirPrev, dirNext);
            if (angleDeg > 179f || angleDeg < 1f)
            {
                // Already straight, or too sharp/degenerate to fillet safely - keep the corner as is.
                rounded.Add(corner);
                continue;
            }

            float halfAngle = angleDeg * 0.5f * Mathf.Deg2Rad;
            float tangentDistance = Mathf.Min(radius / Mathf.Tan(halfAngle), prevLen * 0.49f, nextLen * 0.49f);
            float appliedRadius = tangentDistance * Mathf.Tan(halfAngle);

            Vector2 bisector = dirPrev + dirNext;
            if (bisector.sqrMagnitude < 1e-8f)
            {
                rounded.Add(corner);
                continue;
            }
            bisector.Normalize();

            Vector2 tangentA = corner + dirPrev * tangentDistance;
            Vector2 tangentB = corner + dirNext * tangentDistance;
            Vector2 center = corner + bisector * (appliedRadius / Mathf.Sin(halfAngle));

            float startAngle = Mathf.Atan2(tangentA.y - center.y, tangentA.x - center.x) * Mathf.Rad2Deg;
            float endAngle = Mathf.Atan2(tangentB.y - center.y, tangentB.x - center.x) * Mathf.Rad2Deg;
            float sweep = Mathf.DeltaAngle(startAngle, endAngle);

            for (int s = 0; s <= segments; s++)
            {
                float angle = (startAngle + sweep * s / segments) * Mathf.Deg2Rad;
                rounded.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * appliedRadius);
            }
        }

        return rounded;
    }

    private readonly struct SubmeshGeometry
    {
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        public readonly int[] Triangles;

        public SubmeshGeometry(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            Triangles = triangles;
        }
    }

    private SubmeshGeometry BuildSubmeshGeometry(Tess tess)
    {
        float waterY = transform.position.y + waterSurfaceOffset;

        var worldVertices = new Vector3[tess.VertexCount];
        var uvs = new Vector2[tess.VertexCount];

        for (int i = 0; i < tess.VertexCount; i++)
        {
            Vec3 position = tess.Vertices[i].Position;
            worldVertices[i] = new Vector3(position.X, waterY, position.Y);
            uvs[i] = new Vector2(position.X, position.Y);
        }

        var triangles = new int[tess.ElementCount * 3];
        System.Array.Copy(tess.Elements, triangles, triangles.Length);
        EnsureUpwardWinding(worldVertices, triangles);

        // Mesh vertices/normals must be local to this GameObject's own transform - Unity
        // re-applies transform.localToWorldMatrix (parent position/scale/rotation included) at
        // render time, so storing the already-world-space contour positions directly would double
        // up any parent offset instead of landing exactly on it.
        var localVertices = new Vector3[worldVertices.Length];
        var normals = new Vector3[worldVertices.Length];
        Vector3 localUp = transform.InverseTransformDirection(Vector3.up);

        for (int i = 0; i < worldVertices.Length; i++)
        {
            localVertices[i] = transform.InverseTransformPoint(worldVertices[i]);
            normals[i] = localUp;
        }

        return new SubmeshGeometry(localVertices, normals, uvs, triangles);
    }

    // LibTessDotNet's output winding isn't guaranteed to match Unity's front-face convention -
    // rather than hand-derive the chirality, just measure it: every triangle here comes from the
    // same flat tessellation, so one cross product tells us whether the whole batch faces down and
    // needs flipping.
    private static void EnsureUpwardWinding(Vector3[] vertices, int[] triangles)
    {
        if (triangles.Length < 3)
        {
            return;
        }

        Vector3 a = vertices[triangles[0]];
        Vector3 b = vertices[triangles[1]];
        Vector3 c = vertices[triangles[2]];

        if (Vector3.Cross(b - a, c - a).y >= 0f)
        {
            return;
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }
    }

    // Two submeshes sharing one vertex buffer - water body first, foam ring second, with the
    // foam's triangle indices offset past the water vertices.
    private static Mesh CombineSubmeshes(SubmeshGeometry water, SubmeshGeometry foam)
    {
        int vertexCount = water.Vertices.Length + foam.Vertices.Length;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];

        System.Array.Copy(water.Vertices, vertices, water.Vertices.Length);
        System.Array.Copy(foam.Vertices, 0, vertices, water.Vertices.Length, foam.Vertices.Length);
        System.Array.Copy(water.Normals, normals, water.Normals.Length);
        System.Array.Copy(foam.Normals, 0, normals, water.Normals.Length, foam.Normals.Length);
        System.Array.Copy(water.Uvs, uvs, water.Uvs.Length);
        System.Array.Copy(foam.Uvs, 0, uvs, water.Uvs.Length, foam.Uvs.Length);

        var foamTriangles = new int[foam.Triangles.Length];
        for (int i = 0; i < foamTriangles.Length; i++)
        {
            foamTriangles[i] = foam.Triangles[i] + water.Vertices.Length;
        }

        var mesh = new Mesh { name = "Lake" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(water.Triangles, 0);
        mesh.SetTriangles(foamTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    private void HideOwnVisuals()
    {
        MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
        if (ownRenderer != null)
        {
            ownRenderer.enabled = false;
        }
    }

    private void EnsureStructure()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
        }

        meshRenderer.enabled = true;
    }

    // Reads the box's real local-space mesh bounds instead of assuming any fixed size/pivot
    // convention (e.g. a "1x1x1 unit cube") - this is what makes the footprint correct regardless
    // of this cube's own scale, its parent's position/scale, or the mesh's actual authored size,
    // since it's captured once from the untouched placeholder box before Generate() ever swaps
    // meshFilter.sharedMesh for the merged lake mesh.
    private void CapturePlaceholder()
    {
        if (placeholderCaptured)
        {
            return;
        }

        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            return;
        }

        placeholderMesh = filter.sharedMesh;
        placeholderLocalBounds = filter.sharedMesh.bounds;
        placeholderCaptured = true;
    }

    // World-space XZ footprint of this cube - only X/Z matter for merging lake shapes, the lake
    // itself sits flat at this cube's own Y (see BuildSubmeshGeometry). transform.TransformPoint
    // (not localScale added by hand) is what makes this correct under any scale/rotation, on this
    // object or any parent above it.
    private Rect GetWorldFootprint()
    {
        CapturePlaceholder();
        Bounds localBounds = placeholderCaptured ? placeholderLocalBounds : new Bounds(Vector3.one * 0.5f, Vector3.one);

        Vector3 c0 = transform.TransformPoint(new Vector3(localBounds.min.x, 0f, localBounds.min.z));
        Vector3 c1 = transform.TransformPoint(new Vector3(localBounds.max.x, 0f, localBounds.min.z));
        Vector3 c2 = transform.TransformPoint(new Vector3(localBounds.max.x, 0f, localBounds.max.z));
        Vector3 c3 = transform.TransformPoint(new Vector3(localBounds.min.x, 0f, localBounds.max.z));

        float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
        float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
        float minZ = Mathf.Min(Mathf.Min(c0.z, c1.z), Mathf.Min(c2.z, c3.z));
        float maxZ = Mathf.Max(Mathf.Max(c0.z, c1.z), Mathf.Max(c2.z, c3.z));

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }
}
