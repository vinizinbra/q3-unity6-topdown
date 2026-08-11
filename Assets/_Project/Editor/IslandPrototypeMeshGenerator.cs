using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class IslandPrototypeMeshGenerator
{
    private const string OutputFolder = "Assets/_QuantumUser/View/World/Prototypes";

    private const float CellSize = 2f;
    private const float CellHeight = 1f;

    // Faces whose normal points up at least this much (world Y) count as "top" (platform);
    // everything else (vertical walls, the ramp's slope, undersides) counts as "wall".
    private const float TopNormalThreshold = 0.5f;

    // Rock shape: subdivided so there are enough vertices near the edges/corners for the
    // chamfer to read as a curve and enough spread across faces for the noise to look like
    // rough stone rather than a handful of visible bumps. Convex MeshCollider caps out at 255
    // vertices, so this must stay well under 6 * (subdivisions + 1)^2 = 255 (4 -> 150).
    private const int RockGridSubdivisions = 4;
    private const float RockChamferRadius = 0.16f;
    private const float RockNoiseFrequency = 2.5f;
    private const float RockNoiseAmplitude = 0.05f;
    private const float RockNoiseSeedOffset = 17.3f;

    // Spiky/CliffWall/Pillar are unit-cube (1x1x1) rock variants for scattered decoration, not
    // world-grid pieces, so unlike Rock above they skip the CellSize/CellHeight scale entirely.
    private const int SpikyGridSubdivisions = 5;
    private const float SpikyChamferRadius = 0.04f;
    private const float SpikyNoiseFrequency = 3.2f;
    private const float SpikyNoiseAmplitude = 0.22f;
    private const float SpikyRidgePower = 2.2f;
    private const float SpikySeedOffset = 4.1f;

    private const int CliffWallGridSubdivisions = 5;
    private const float CliffWallChamferRadius = 0.05f;
    private static readonly Vector3 CliffWallFaceNormal = Vector3.back;
    private const float CliffWallFaceNoiseFrequency = 1.6f;
    private const float CliffWallFaceNoiseAmplitude = 0.16f;
    private const float CliffWallDetailNoiseFrequency = 6f;
    private const float CliffWallDetailNoiseAmplitude = 0.03f;
    private const float CliffWallOtherFaceNoiseAmplitude = 0.02f;
    private const float CliffWallSeedOffset = 91.7f;

    private const int PillarGridSubdivisions = 5;
    private const float PillarChamferRadius = 0.08f;
    private const float PillarFluteCount = 7f;
    private const float PillarFluteAmplitude = 0.07f;
    private const float PillarBulgeAmplitude = 0.06f;
    private const float PillarDetailNoiseFrequency = 4f;
    private const float PillarDetailNoiseAmplitude = 0.02f;
    private const float PillarSeedOffset = 53f;

    private const int StalagmiteGridSubdivisions = 5;
    private const float StalagmiteChamferRadius = 0.1f;
    // Fraction of the base width still remaining at the very tip (kept above 0 so the top rounds
    // into a small blunt cap rather than pinching into a single degenerate point).
    private const float StalagmiteTipRadiusScale = 0.08f;
    // >1 keeps the taper close to full width for most of the height and only narrows sharply near
    // the tip - the typical bulbous-cone stalagmite silhouette rather than a straight-sided cone.
    private const float StalagmiteTaperPower = 1.6f;
    private const float StalagmiteRingFrequency = 5f;
    private const float StalagmiteRingAmplitude = 0.025f;
    private const float StalagmiteDetailNoiseFrequency = 4f;
    private const float StalagmiteDetailNoiseAmplitude = 0.015f;
    private const float StalagmiteLeanFrequency = 0.9f;
    private const float StalagmiteLeanAmplitude = 0.05f;
    private const float StalagmiteSeedOffset = 128f;

    [MenuItem("Tools/World/Generate Prototype Prefabs")]
    public static void Generate()
    {
        EnsureFolderExists(OutputFolder);

        // Shared across all three shapes so the height gradient reads as one continuous
        // terrain look level-wide, rather than resetting per block type.
        Material topMaterial = GetOrCreateSharedMaterial($"{OutputFolder}/PlatformTopMaterial.mat");
        Material wallMaterial = GetOrCreateSharedMaterial($"{OutputFolder}/WallMaterial.mat");

        CreatePrototype("Cube", BuildCubeMesh(), topMaterial, wallMaterial, useConvexMeshCollider: false);
        CreatePrototype("Corner", BuildCornerMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);
        CreatePrototype("Ramp", BuildRampMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);
        CreatePrototype("Rock", BuildRockMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandPrototypeMeshGenerator] Generated prototype prefabs in {OutputFolder}");
    }

    [MenuItem("Tools/World/Generate Rock Prototype Prefabs")]
    public static void GenerateRockVariants()
    {
        EnsureFolderExists(OutputFolder);

        Material topMaterial = GetOrCreateSharedMaterial($"{OutputFolder}/PlatformTopMaterial.mat");
        Material wallMaterial = GetOrCreateSharedMaterial($"{OutputFolder}/WallMaterial.mat");

        CreatePrototype("RockSpiky", BuildSpikyRockMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);
        CreatePrototype("RockCliffWall", BuildCliffWallRockMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);
        CreatePrototype("RockPillar", BuildPillarRockMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);
        CreatePrototype("RockStalagmite", BuildStalagmiteRockMesh(), topMaterial, wallMaterial, useConvexMeshCollider: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandPrototypeMeshGenerator] Generated rock prototype prefabs in {OutputFolder}");
    }

    private static Material GetOrCreateSharedMaterial(string path)
    {
        // Reused (not deleted/recreated) across regenerations so hand-tuned color values on
        // these materials survive re-running the generator after a mesh tweak.
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Custom/HeightGradientLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void CreatePrototype(string name, Mesh mesh, Material topMaterial, Material wallMaterial, bool useConvexMeshCollider)
    {
        string meshPath = $"{OutputFolder}/{name}.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { topMaterial, wallMaterial };

        if (useConvexMeshCollider)
        {
            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = true;
        }
        else
        {
            var collider = go.AddComponent<BoxCollider>();
            collider.center = mesh.bounds.center;
            collider.size = mesh.bounds.size;
        }

        string prefabPath = $"{OutputFolder}/{name}.prefab";
        AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
    }

    private static Mesh BuildCubeMesh()
    {
        Mesh mesh = CopyUnitCubeMesh();
        ScaleVertices(mesh, new Vector3(CellSize, CellHeight, CellSize));
        mesh.RecalculateNormals();
        mesh.name = "Cube";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    private static Mesh BuildCornerMesh()
    {
        // Collapse the NW vertical edge onto the SW one: the square footprint (SW,SE,NE,NW)
        // becomes the right triangle (SW,SE,NE) - matches the (South,East)-filled notch from
        // IslandBuilder's CornerPairs at angle 0, with the right angle sitting at SE.
        Mesh mesh = CopyUnitCubeMesh();
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].x < 0f && vertices[i].z > 0f)
            {
                vertices[i].z = -0.5f;
            }
        }

        mesh.vertices = vertices;

        ScaleVertices(mesh, new Vector3(CellSize, CellHeight, CellSize));
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "Corner";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    private static Mesh BuildRampMesh()
    {
        Mesh mesh = CopyUnitCubeMesh();
        Vector3[] vertices = mesh.vertices;

        // Collapse the front-top edge (y > 0, z < 0) down to the bottom, turning the cube into a wedge.
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].y > 0f && vertices[i].z < 0f)
            {
                vertices[i].y = -0.5f;
            }
        }

        mesh.vertices = vertices;

        ScaleVertices(mesh, new Vector3(CellSize, CellHeight, CellSize));
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "Ramp";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    private static Mesh BuildRockMesh()
    {
        Mesh mesh = BuildSubdividedCubeMeshData(RockGridSubdivisions).Mesh;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 chamfered = ChamferCubeVertex(vertices[i], RockChamferRadius);
            vertices[i] = chamfered + RockNoiseDisplacement(chamfered);
        }

        mesh.vertices = vertices;
        ScaleVertices(mesh, new Vector3(CellSize, CellHeight, CellSize));
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "Rock";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    private static Vector3 RockNoiseDisplacement(Vector3 position)
    {
        float noise = SampleNoise3D(position, RockNoiseFrequency, RockNoiseSeedOffset);
        return position.normalized * (noise * RockNoiseAmplitude);
    }

    // Sharp, isolated protrusions: ridged noise stays near 1 only around its former zero-crossings,
    // and raising it to a power thins those ridges down to points - then only ever pushed outward
    // (never carved in) so the base silhouette stays intact between spikes.
    private static Mesh BuildSpikyRockMesh()
    {
        Mesh mesh = BuildSubdividedCubeMeshData(SpikyGridSubdivisions).Mesh;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 chamfered = ChamferCubeVertex(vertices[i], SpikyChamferRadius);
            float ridge = SampleRidgedNoise3D(chamfered, SpikyNoiseFrequency, SpikySeedOffset);
            float spike = Mathf.Pow(Mathf.Max(ridge, 0f), SpikyRidgePower);
            vertices[i] = chamfered + chamfered.normalized * (spike * SpikyNoiseAmplitude);
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "RockSpiky";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    // A flat, mostly-untouched block with one marked "exposed" face carved up by a large-scale
    // noise layer (rock strata) plus a fine one (surface cracks) - meant to butt against other
    // CliffWall pieces on the untouched sides to read as one continuous rock face.
    private static Mesh BuildCliffWallRockMesh()
    {
        SubdividedCubeMeshData data = BuildSubdividedCubeMeshData(CliffWallGridSubdivisions);
        Vector3[] vertices = data.Mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 chamfered = ChamferCubeVertex(vertices[i], CliffWallChamferRadius);
            bool isExposedFace = Vector3.Dot(data.FaceNormals[i], CliffWallFaceNormal) > 0.5f;

            float bigNoise = SampleNoise3D(chamfered, CliffWallFaceNoiseFrequency, CliffWallSeedOffset);
            float displacement = bigNoise * (isExposedFace ? CliffWallFaceNoiseAmplitude : CliffWallOtherFaceNoiseAmplitude);

            if (isExposedFace)
            {
                float detailNoise = SampleNoise3D(chamfered, CliffWallDetailNoiseFrequency, CliffWallSeedOffset + 31f);
                displacement += detailNoise * CliffWallDetailNoiseAmplitude;
            }

            vertices[i] = chamfered + chamfered.normalized * displacement;
        }

        data.Mesh.vertices = vertices;
        data.Mesh.RecalculateNormals();
        data.Mesh.RecalculateBounds();
        data.Mesh.name = "RockCliffWall";
        SplitTopAndWallSubmeshes(data.Mesh);
        return data.Mesh;
    }

    // Vertical flutes driven by angle-around-Y (not per-vertex noise direction) so the grooves
    // line up into continuous ridges rather than random bumps, plus a bulge that peaks at
    // mid-height and fades to zero at the top/bottom rims - reads as an eroded stone column.
    private static Mesh BuildPillarRockMesh()
    {
        Mesh mesh = BuildSubdividedCubeMeshData(PillarGridSubdivisions).Mesh;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 chamfered = ChamferCubeVertex(vertices[i], PillarChamferRadius);

            float angle = Mathf.Atan2(chamfered.z, chamfered.x);
            float flute = Mathf.Sin(angle * PillarFluteCount) * PillarFluteAmplitude;
            float bulge = Mathf.Sin(Mathf.PI * (chamfered.y + 0.5f)) * PillarBulgeAmplitude;
            float detail = SampleNoise3D(chamfered, PillarDetailNoiseFrequency, PillarSeedOffset) * PillarDetailNoiseAmplitude;

            Vector3 radial = new Vector3(chamfered.x, 0f, chamfered.z);
            Vector3 radialDir = radial.sqrMagnitude > 1e-8f ? radial.normalized : Vector3.zero;

            vertices[i] = chamfered + radialDir * (flute + bulge + detail);
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "RockPillar";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    // A cone that stays close to full width for most of its height and only narrows sharply near
    // the tip (StalagmiteTaperPower), banded by horizontal "accretion rings" (a sine of height
    // alone, not angle - unlike Pillar's vertical flutes) and a gentle low-frequency lean so it
    // doesn't read as a perfectly straight, symmetrical cone.
    private static Mesh BuildStalagmiteRockMesh()
    {
        Mesh mesh = BuildSubdividedCubeMeshData(StalagmiteGridSubdivisions).Mesh;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 chamfered = ChamferCubeVertex(vertices[i], StalagmiteChamferRadius);

            float t = Mathf.Clamp01(chamfered.y + 0.5f); // 0 at the base, 1 at the tip
            float taper = Mathf.Lerp(1f, StalagmiteTipRadiusScale, Mathf.Pow(t, StalagmiteTaperPower));

            float ring = Mathf.Sin(t * StalagmiteRingFrequency * Mathf.PI * 2f) * StalagmiteRingAmplitude;
            float detail = SampleNoise3D(chamfered, StalagmiteDetailNoiseFrequency, StalagmiteSeedOffset) * StalagmiteDetailNoiseAmplitude;

            float leanX = Mathf.Sin(t * StalagmiteLeanFrequency * Mathf.PI + StalagmiteSeedOffset) * StalagmiteLeanAmplitude;
            float leanZ = Mathf.Sin(t * StalagmiteLeanFrequency * Mathf.PI + StalagmiteSeedOffset + 1.7f) * StalagmiteLeanAmplitude * 0.7f;

            Vector3 radial = new Vector3(chamfered.x, 0f, chamfered.z);
            Vector3 radialDir = radial.sqrMagnitude > 1e-8f ? radial.normalized : Vector3.zero;

            Vector3 tapered = new Vector3(chamfered.x * taper, chamfered.y, chamfered.z * taper);
            tapered += radialDir * (ring + detail);
            tapered.x += leanX;
            tapered.z += leanZ;

            vertices[i] = tapered;
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "RockStalagmite";
        SplitTopAndWallSubmeshes(mesh);
        return mesh;
    }

    // Pushes a unit-cube (half-extent 0.5) vertex onto a rounded-box surface: vertices already
    // inside the shrunk "inner box" (face centers) are untouched, vertices outside it (near an
    // edge or corner) are clamped onto the inner box and then pushed back out by exactly
    // `radius` - the standard rounded-box construction, so edges/corners read as beveled arcs
    // instead of sharp creases.
    private static Vector3 ChamferCubeVertex(Vector3 position, float radius)
    {
        float innerExtent = 0.5f - radius;
        Vector3 clamped = new Vector3(
            Mathf.Clamp(position.x, -innerExtent, innerExtent),
            Mathf.Clamp(position.y, -innerExtent, innerExtent),
            Mathf.Clamp(position.z, -innerExtent, innerExtent));

        Vector3 offset = position - clamped;
        if (offset.sqrMagnitude < 1e-8f)
        {
            return position;
        }

        return clamped + offset.normalized * radius;
    }

    // Cheap 3D value noise built from three 2D Perlin samples (no full simplex-noise dependency
    // needed for a low-poly prop).
    private static float SampleNoise3D(Vector3 position, float frequency, float seedOffset)
    {
        float nx = position.x * frequency + seedOffset;
        float ny = position.y * frequency + seedOffset;
        float nz = position.z * frequency + seedOffset;

        float noise = Mathf.PerlinNoise(ny, nz) + Mathf.PerlinNoise(nz, nx) + Mathf.PerlinNoise(nx, ny);
        return (noise / 3f - 0.5f) * 2f; // remap average of three [0,1] samples to [-1,1]
    }

    // Folds the value noise around its midpoint so former "up/down slopes" become sharp ridges
    // at the old zero-crossings instead of smooth bumps - the standard "ridged noise" trick used
    // for jagged terrain, here reused for the Spiky rock's isolated points.
    private static float SampleRidgedNoise3D(Vector3 position, float frequency, float seedOffset)
    {
        return 1f - Mathf.Abs(SampleNoise3D(position, frequency, seedOffset));
    }

    private readonly struct SubdividedCubeMeshData
    {
        public readonly Mesh Mesh;
        public readonly Vector3[] FaceNormals;

        public SubdividedCubeMeshData(Mesh mesh, Vector3[] faceNormals)
        {
            Mesh = mesh;
            FaceNormals = faceNormals;
        }
    }

    // Builds an axis-aligned unit cube (half-extent 0.5) as 6 independent NxN vertex grids (one
    // per face, not welded across face boundaries) so ChamferCubeVertex/noise displacement have
    // enough vertices near each edge/corner to bend into a curve, and so RecalculateNormals keeps
    // a faceted look between faces while still smoothing across each face's own grid. FaceNormals
    // records which of the 6 box faces each vertex started on, in the same order as Mesh.vertices,
    // for effects (like CliffWall's single "exposed" face) that need to treat faces differently.
    private static SubdividedCubeMeshData BuildSubdividedCubeMeshData(int subdivisions)
    {
        var vertices = new List<Vector3>();
        var faceNormalsPerVertex = new List<Vector3>();
        var triangles = new List<int>();
        Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        foreach (Vector3 normal in faceNormals)
        {
            int startIndex = vertices.Count;
            BuildSubdividedFace(normal, subdivisions, vertices, triangles);
            for (int i = startIndex; i < vertices.Count; i++)
            {
                faceNormalsPerVertex.Add(normal);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        return new SubdividedCubeMeshData(mesh, faceNormalsPerVertex.ToArray());
    }

    private static void BuildSubdividedFace(Vector3 normal, int subdivisions, List<Vector3> vertices, List<int> triangles)
    {
        // Cross products guarantee tangentU/tangentV/normal form a consistent right-handed
        // basis for every face, which is what keeps the (i0,i1,i2)/(i1,i3,i2) winding below
        // outward-facing (Unity's face normal is cross(v1-v0, v2-v0), which points along
        // +normal for this winding given how tangentV = normal x tangentU is built) on all 6
        // faces without needing a per-axis special case.
        Vector3 tangentU = Mathf.Abs(normal.y) < 0.99f ? Vector3.Cross(Vector3.up, normal) : Vector3.Cross(Vector3.forward, normal);
        Vector3 tangentV = Vector3.Cross(normal, tangentU);

        int vertsPerRow = subdivisions + 1;
        int startIndex = vertices.Count;

        for (int row = 0; row <= subdivisions; row++)
        {
            for (int col = 0; col <= subdivisions; col++)
            {
                float u = (float)col / subdivisions - 0.5f;
                float v = (float)row / subdivisions - 0.5f;
                vertices.Add(normal * 0.5f + tangentU * u + tangentV * v);
            }
        }

        for (int row = 0; row < subdivisions; row++)
        {
            for (int col = 0; col < subdivisions; col++)
            {
                int i0 = startIndex + row * vertsPerRow + col;
                int i1 = i0 + 1;
                int i2 = i0 + vertsPerRow;
                int i3 = i2 + 1;

                triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
            }
        }
    }

    private static void SplitTopAndWallSubmeshes(Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        var topTriangles = new List<int>();
        var wallTriangles = new List<int>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            float faceNormalY = (normals[a].y + normals[b].y + normals[c].y) / 3f;

            List<int> target = faceNormalY > TopNormalThreshold ? topTriangles : wallTriangles;
            target.Add(a);
            target.Add(b);
            target.Add(c);
        }

        mesh.subMeshCount = 2;
        mesh.SetTriangles(topTriangles, 0);
        mesh.SetTriangles(wallTriangles, 1);
    }

    private static Mesh CopyUnitCubeMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh copy = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(temp);
        return copy;
    }

    private static void ScaleVertices(Mesh mesh, Vector3 scale)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Vector3.Scale(vertices[i], scale);
        }

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    private static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
