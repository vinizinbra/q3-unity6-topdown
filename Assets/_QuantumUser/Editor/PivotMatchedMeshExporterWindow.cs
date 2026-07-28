using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuantumUser.Editor
{
    public sealed class PivotMatchedMeshExporterWindow : EditorWindow
    {
        private const string OutputRoot = "Assets/_Project/Art/PivotFixed";

        [SerializeField] private Transform _pivot;
        [SerializeField] private MeshFilter _source;
        [SerializeField] private string _outputName;
        [SerializeField] private bool _bakeWorldRotationAndScale = true;
        private bool _batchExporting;

        [MenuItem("Tools/Art/Export Mesh With Matched Pivot")]
        private static void Open()
        {
            var window = GetWindow<PivotMatchedMeshExporterWindow>("Match Mesh Pivot");
            window.minSize = new Vector2(430f, 220f);
            window.UseSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Bakes the source mesh into the Pivot Object's local space. The exported prefab's origin " +
                "then matches that pivot while reproducing the child's current position, rotation, and scale.",
                MessageType.Info);

            _pivot = (Transform)EditorGUILayout.ObjectField("Pivot Object", _pivot, typeof(Transform), true);
            _source = (MeshFilter)EditorGUILayout.ObjectField("Source Child Mesh", _source, typeof(MeshFilter), true);
            _outputName = EditorGUILayout.TextField("Output Name", _outputName);
            _bakeWorldRotationAndScale = EditorGUILayout.Toggle(
                new GUIContent("Bake World Rotation + Scale",
                    "Enabled: exported prefab uses identity rotation/scale and is placed at the pivot position. " +
                    "Disabled: exported mesh remains aligned to the pivot's local axes."),
                _bakeWorldRotationAndScale);

            GUILayout.FlexibleSpace();
            bool valid = _pivot != null && _source != null && _source.sharedMesh != null &&
                         _source.gameObject.activeInHierarchy && _source.transform.IsChildOf(_pivot) &&
                         _source.transform != _pivot;
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Export Selected Child", GUILayout.Height(30f)))
                    Export();
            }
            using (new EditorGUI.DisabledScope(_pivot == null || FindActiveChildMeshes(_pivot).Count == 0))
            {
                if (GUILayout.Button("Export Every Active Child Mesh", GUILayout.Height(36f)))
                    ExportAllActiveChildren();
            }
        }

        private void OnSelectionChange()
        {
            if (_batchExporting)
                return;
            UseSelection();
            Repaint();
        }

        private void UseSelection()
        {
            GameObject active = Selection.activeGameObject;
            if (active == null)
                return;

            MeshFilter activeMesh = active.GetComponent<MeshFilter>();
            if (activeMesh != null && active.transform.parent != null)
            {
                _source = activeMesh;
                _pivot = active.transform.parent;
            }
            else
            {
                _pivot = active.transform;
                _source = FindActiveChildMesh(_pivot);
            }

            if (_source != null)
                _outputName = _source.sharedMesh != null ? _source.sharedMesh.name + "_PivotFixed" : _source.name + "_PivotFixed";
        }

        private static MeshFilter FindActiveChildMesh(Transform pivot)
        {
            List<MeshFilter> meshes = FindActiveChildMeshes(pivot);
            return meshes.Count > 0 ? meshes[0] : null;
        }

        private static List<MeshFilter> FindActiveChildMeshes(Transform pivot)
        {
            var result = new List<MeshFilter>();
            MeshFilter[] meshes = pivot.GetComponentsInChildren<MeshFilter>(false);
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i].transform != pivot && meshes[i].gameObject.activeInHierarchy && meshes[i].sharedMesh != null)
                    result.Add(meshes[i]);
            return result;
        }

        private void ExportAllActiveChildren()
        {
            List<MeshFilter> children = FindActiveChildMeshes(_pivot);
            Transform batchPivot = _pivot;
            MeshFilter previousSource = _source;
            string previousName = _outputName;
            _batchExporting = true;
            try
            {
                for (int i = 0; i < children.Count; i++)
                {
                    _pivot = batchPivot;
                    _source = children[i];
                    _outputName = children[i].gameObject.name + "_PivotFixed";
                    Export();
                }
            }
            finally
            {
                _batchExporting = false;
                _pivot = batchPivot;
                _source = previousSource;
                _outputName = previousName;
            }
            Debug.Log($"Exported {children.Count} active child mesh prefab(s) using pivot '{batchPivot.name}'.", this);
        }

        private void Export()
        {
            EnsureFolder(OutputRoot);
            EnsureFolder(OutputRoot + "/Meshes");
            EnsureFolder(OutputRoot + "/Prefabs");

            string safeName = SafeName(string.IsNullOrWhiteSpace(_outputName)
                ? _source.sharedMesh.name + "_PivotFixed"
                : _outputName.Trim());
            string meshPath = $"{OutputRoot}/Meshes/{safeName}.asset";
            string prefabPath = $"{OutputRoot}/Prefabs/{safeName}.prefab";

            Matrix4x4 sourceToNewPivot;
            if (_bakeWorldRotationAndScale)
            {
                // Preserve the current world orientation/size, but move the origin to the
                // reference pivot position. The resulting prefab can use rotation identity
                // and scale one when instantiated at that position.
                sourceToNewPivot = Matrix4x4.Translate(-_pivot.position) * _source.transform.localToWorldMatrix;
            }
            else
            {
                // Preserve coordinates relative to the complete pivot transform. Instantiate
                // with the pivot's position, rotation, and scale to reproduce the source.
                sourceToNewPivot = _pivot.worldToLocalMatrix * _source.transform.localToWorldMatrix;
            }

            Mesh baked = BuildMesh(_source.sharedMesh, sourceToNewPivot, safeName);
            Mesh savedMesh = SaveOrUpdateMesh(baked, meshPath);
            Material[] materials = Array.Empty<Material>();
            MeshRenderer sourceRenderer = _source.GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
                materials = sourceRenderer.sharedMaterials;

            var root = new GameObject(safeName);
            root.AddComponent<MeshFilter>().sharedMesh = savedMesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            if (sourceRenderer != null)
            {
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            if (!_batchExporting)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            Debug.Log($"Exported '{safeName}' with pivot matching '{_pivot.name}' to '{prefabPath}'.", prefab);
        }

        private static Mesh BuildMesh(Mesh source, Matrix4x4 sourceToPivot, string meshName)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Matrix4x4 normalMatrix = sourceToPivot.inverse.transpose;
            float tangentHandedness = sourceToPivot.determinant < 0f ? -1f : 1f;

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = sourceToPivot.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++)
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 direction = sourceToPivot.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w * tangentHandedness);
            }

            var output = new Mesh
            {
                name = meshName,
                indexFormat = source.indexFormat == IndexFormat.UInt32 || vertices.Length > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                tangents = tangents,
                colors = source.colors,
                uv = source.uv,
                uv2 = source.uv2,
                uv3 = source.uv3,
                uv4 = source.uv4,
                subMeshCount = source.subMeshCount
            };
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                output.SetIndices(source.GetIndices(subMesh), source.GetTopology(subMesh), subMesh, false);
            output.RecalculateBounds();
            return output;
        }

        private static Mesh SaveOrUpdateMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            existing.Clear(false);
            existing.name = generated.name;
            existing.indexFormat = generated.indexFormat;
            existing.vertices = generated.vertices;
            existing.normals = generated.normals;
            existing.tangents = generated.tangents;
            existing.colors = generated.colors;
            existing.uv = generated.uv;
            existing.uv2 = generated.uv2;
            existing.uv3 = generated.uv3;
            existing.uv4 = generated.uv4;
            existing.subMeshCount = generated.subMeshCount;
            for (int subMesh = 0; subMesh < generated.subMeshCount; subMesh++)
                existing.SetIndices(generated.GetIndices(subMesh), generated.GetTopology(subMesh), subMesh, false);
            existing.bounds = generated.bounds;
            EditorUtility.SetDirty(existing);
            DestroyImmediate(generated);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string SafeName(string value)
        {
            foreach (char character in Path.GetInvalidFileNameChars())
                value = value.Replace(character, '_');
            return value;
        }
    }
}
