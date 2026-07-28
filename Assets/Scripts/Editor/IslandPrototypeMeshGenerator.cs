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

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandPrototypeMeshGenerator] Generated prototype prefabs in {OutputFolder}");
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
