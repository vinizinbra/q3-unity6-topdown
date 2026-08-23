using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using QuantumUser.View.Util;

namespace QuantumUser.Editor
{
    /// <summary>
    /// Bakes a position-averaged object-space normal into UV channel 3 (TEXCOORD3), which
    /// Project/Borderlands Toon Modular Level's inverted-hull outline pass extrudes along.
    ///
    /// ModularLevelConverterWindow reuses the source mesh's indexed topology, so a hard edge on a
    /// modular piece is still two vertices at the same position carrying two different normals.
    /// Extruding a hull along those splits the shell into disconnected faces and opens a gap at
    /// every corner. Averaging by position closes the shell while leaving NORMAL - and therefore
    /// the lit pass's shading - completely untouched.
    /// </summary>
    public static class SmoothNormalBakerWindow
    {
        public const int SmoothNormalUvChannel = 3;

        [MenuItem("Tools/Art/Bake Outline Smooth Normals")]
        private static void BakeSelection()
        {
            List<Mesh> meshes = CollectSelectedMeshes();
            if (meshes.Count == 0)
            {
                EditorUtility.DisplayDialog("Outline Smooth Normals",
                    "Select one or more Mesh assets, or GameObjects with a MeshFilter/SkinnedMeshRenderer.", "OK");
                return;
            }

            int baked = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                if (!AssetDatabase.Contains(meshes[i]))
                {
                    LogHelper.Warn("SmoothNormalBaker",
                        $"'{meshes[i].name}' is not a saved asset - a runtime mesh instance cannot be baked.", meshes[i]);
                    continue;
                }

                if (!Bake(meshes[i]))
                    continue;

                EditorUtility.SetDirty(meshes[i]);
                baked++;
            }

            AssetDatabase.SaveAssets();
            LogHelper.Log("SmoothNormalBaker", $"Baked outline smooth normals into UV{SmoothNormalUvChannel} on {baked} mesh(es).");
        }

        [MenuItem("Tools/Art/Bake Outline Smooth Normals", true)]
        private static bool ValidateBakeSelection() => !Application.isPlaying;

        /// <summary>
        /// Writes the averaged object-space normals into <see cref="SmoothNormalUvChannel"/>.
        /// Returns false when the mesh carries no normals to average.
        /// </summary>
        public static bool Bake(Mesh mesh)
        {
            if (mesh == null)
                return false;

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (positions.Length == 0 || normals.Length != positions.Length)
            {
                LogHelper.Warn("SmoothNormalBaker", $"'{mesh.name}' has no per-vertex normals to average.", mesh);
                return false;
            }

            // Quantise before keying so vertices that are coincident in authoring but differ by a
            // float rounding step still merge. 1e-4 of a world unit is far below any modular seam.
            var accumulated = new Dictionary<Vector3Int, Vector3>(positions.Length);
            var keys = new Vector3Int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3Int key = Quantise(positions[i]);
                keys[i] = key;
                accumulated[key] = accumulated.TryGetValue(key, out Vector3 sum) ? sum + normals[i] : normals[i];
            }

            var smoothNormals = new List<Vector3>(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 sum = accumulated[keys[i]];
                // A perfectly opposed pair (a zero-thickness fin) cancels to zero. Fall back to the
                // real normal there rather than emitting a direction the hull cannot extrude along.
                smoothNormals.Add(sum.sqrMagnitude > 1e-10f ? sum.normalized : normals[i]);
            }

            mesh.SetUVs(SmoothNormalUvChannel, smoothNormals);
            return true;
        }

        private static Vector3Int Quantise(Vector3 position)
        {
            const float scale = 10000f;
            return new Vector3Int(
                Mathf.RoundToInt(position.x * scale),
                Mathf.RoundToInt(position.y * scale),
                Mathf.RoundToInt(position.z * scale));
        }

        private static List<Mesh> CollectSelectedMeshes()
        {
            var result = new List<Mesh>();
            var seen = new HashSet<Mesh>();

            foreach (Object selected in Selection.objects)
            {
                if (selected is Mesh directMesh && seen.Add(directMesh))
                    result.Add(directMesh);

                if (selected is not GameObject root)
                    continue;

                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && seen.Add(filter.sharedMesh))
                        result.Add(filter.sharedMesh);

                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh != null && seen.Add(renderer.sharedMesh))
                        result.Add(renderer.sharedMesh);
            }

            return result;
        }
    }
}
