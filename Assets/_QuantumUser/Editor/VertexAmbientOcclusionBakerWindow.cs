using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using QuantumUser.View.Util;

namespace QuantumUser.Editor
{
    public sealed class VertexAmbientOcclusionBakerWindow : EditorWindow
    {
        [SerializeField, Range(8, 128)] private int _raysPerVertex = 32;
        [SerializeField, Min(0.01f)] private float _maxDistance = 2f;
        [SerializeField, Min(0.0001f)] private float _surfaceBias = 0.005f;
        [SerializeField, Range(0.1f, 4f)] private float _bakeContrast = 1f;

        [MenuItem("Tools/Art/Bake Vertex Ambient Occlusion")]
        private static void Open()
        {
            var window = GetWindow<VertexAmbientOcclusionBakerWindow>("Vertex AO Baker");
            window.minSize = new Vector2(420f, 235f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Bakes self-occlusion into vertex color G on the meshes referenced by selected prefabs or scene objects. " +
                "Vertex color R is preserved for wall/surface classification. Generated mesh assets are updated in place.",
                MessageType.Info);
            _raysPerVertex = EditorGUILayout.IntSlider("Rays Per Vertex", _raysPerVertex, 8, 128);
            _maxDistance = EditorGUILayout.FloatField("Occlusion Distance", _maxDistance);
            _surfaceBias = EditorGUILayout.FloatField("Surface Bias", _surfaceBias);
            _bakeContrast = EditorGUILayout.Slider("Bake Contrast", _bakeContrast, 0.1f, 4f);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button("Bake AO Into Selected Meshes", GUILayout.Height(38f)))
                    BakeSelected();
            }
        }

        private void BakeSelected()
        {
            List<Mesh> meshes = CollectSelectedMeshes();
            if (meshes.Count == 0)
            {
                EditorUtility.DisplayDialog("Vertex AO Baker", "No readable MeshFilter meshes were found in the selection.", "OK");
                return;
            }

            var temporary = new GameObject("Vertex AO Temporary Collider")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            MeshCollider collider = temporary.AddComponent<MeshCollider>();
            bool previousHitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                {
                    Mesh mesh = meshes[meshIndex];
                    EditorUtility.DisplayProgressBar("Baking vertex ambient occlusion",
                        $"{mesh.name} ({meshIndex + 1}/{meshes.Count})", (float)meshIndex / meshes.Count);
                    BakeMesh(mesh, collider);
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = previousHitBackfaces;
                DestroyImmediate(temporary);
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            LogHelper.Log("VertexAOBaker", $"Baked vertex AO into color G for {meshes.Count} mesh asset(s).", this);
        }

        private void BakeMesh(Mesh mesh, MeshCollider collider)
        {
            Vector3[] vertices;
            Vector3[] normals;
            try
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
            }
            catch (UnityException exception)
            {
                LogHelper.Warn("VertexAOBaker", $"Skipping unreadable mesh '{mesh.name}': {exception.Message}", mesh);
                return;
            }
            if (vertices.Length == 0)
                return;
            if (normals.Length != vertices.Length)
            {
                LogHelper.Warn("VertexAOBaker", $"Skipping '{mesh.name}' because it has no complete normal channel.", mesh);
                return;
            }

            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            Color[] colors = mesh.colors;
            if (colors.Length != vertices.Length)
            {
                colors = new Color[vertices.Length];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = Color.white;
            }

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Vector3 normal = normals[vertexIndex].normalized;
                BuildBasis(normal, out Vector3 tangent, out Vector3 bitangent);
                Vector3 origin = vertices[vertexIndex] + normal * _surfaceBias;
                int hits = 0;
                for (int rayIndex = 0; rayIndex < _raysPerVertex; rayIndex++)
                {
                    Vector3 sample = HemisphereSample(rayIndex, _raysPerVertex);
                    Vector3 direction = (tangent * sample.x + bitangent * sample.y + normal * sample.z).normalized;
                    if (collider.Raycast(new Ray(origin, direction), out _, _maxDistance))
                        hits++;
                }

                float ao = 1f - (float)hits / _raysPerVertex;
                ao = Mathf.Pow(Mathf.Clamp01(ao), _bakeContrast);
                Color color = colors[vertexIndex];
                color.g = ao;
                colors[vertexIndex] = color;
            }

            Undo.RecordObject(mesh, "Bake vertex ambient occlusion");
            mesh.colors = colors;
            EditorUtility.SetDirty(mesh);
        }

        private static Vector3 HemisphereSample(int index, int count)
        {
            const float goldenAngle = 2.39996323f;
            float z = (index + 0.5f) / count;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float angle = index * goldenAngle;
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
        }

        private static void BuildBasis(Vector3 normal, out Vector3 tangent, out Vector3 bitangent)
        {
            Vector3 helper = Mathf.Abs(normal.y) < 0.999f ? Vector3.up : Vector3.right;
            tangent = Vector3.Cross(helper, normal).normalized;
            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private static List<Mesh> CollectSelectedMeshes()
        {
            var result = new List<Mesh>();
            var seen = new HashSet<Mesh>();
            foreach (GameObject selected in Selection.gameObjects)
            foreach (MeshFilter filter in selected.GetComponentsInChildren<MeshFilter>(true))
            {
                string path = filter.sharedMesh != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : string.Empty;
                if (filter.sharedMesh != null && path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase) && seen.Add(filter.sharedMesh))
                    result.Add(filter.sharedMesh);
            }
            return result;
        }
    }
}
