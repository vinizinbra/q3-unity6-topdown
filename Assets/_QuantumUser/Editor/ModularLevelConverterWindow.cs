using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuantumUser.Editor
{
    public sealed class ModularLevelConverterWindow : EditorWindow
    {
        private const string Root = "Assets/_Project/Art/ModularLevel";
        private const string SourceMasks = "Assets/_Project/Art/InkTextures";
        [SerializeField] private int _wallTileSize = 512;
        [SerializeField] private int _surfaceTileSize = 256;

        [MenuItem("Tools/Art/Convert Selected Modular Level")]
        private static void Open() => GetWindow<ModularLevelConverterWindow>("Modular Converter");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Stage 2 only: consumes the approved masks created by UV2 Ink Mask Baker, then creates the atlas, merged meshes, shared material, and prefabs.", MessageType.Info);
            _wallTileSize = EditorGUILayout.IntPopup("Wall Detail Resolution", _wallTileSize,
                new[] { "128 (Low)", "256 (Medium)", "512 (High)", "1024 (Very High)" },
                new[] { 128, 256, 512, 1024 });
            _surfaceTileSize = EditorGUILayout.IntPopup("Surface Fade Resolution", _surfaceTileSize,
                new[] { "64 (Low)", "128 (Medium)", "256 (High)", "512 (Very High)" },
                new[] { 64, 128, 256, 512 });
            int selectedCount = Mathf.Max(1, CollectSources().Count);
            int previewColumns = Mathf.CeilToInt(Mathf.Sqrt(selectedCount));
            int previewRows = Mathf.CeilToInt((float)selectedCount / previewColumns);
            int previewWidth = Mathf.NextPowerOfTwo(previewColumns * (_wallTileSize + _surfaceTileSize));
            int previewHeight = Mathf.NextPowerOfTwo(previewRows * Mathf.Max(_wallTileSize, _surfaceTileSize));
            EditorGUILayout.LabelField("Generated Atlas", $"{previewWidth} x {previewHeight}");
            if (previewWidth > 8192 || previewHeight > 8192)
                EditorGUILayout.HelpBox("This selection produces an atlas above 8K. It can use substantial memory on mobile.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button("Regenerate Atlas Only", GUILayout.Height(32))) ConvertSelected(false);
                if (GUILayout.Button("Convert Selected Objects", GUILayout.Height(36))) ConvertSelected(true);
            }
        }

        private void ConvertSelected(bool rebuildMeshesAndPrefabs)
        {
            List<Source> sources = CollectSources();
            if (sources.Count == 0) { EditorUtility.DisplayDialog("Modular Converter", "No selected one- or two-material modular prefabs were found.", "OK"); return; }
            sources.Sort((a, b) => string.CompareOrdinal(SourceKey(a.Mesh), SourceKey(b.Mesh)));
            EnsureFolders();
            var missingMasks = new List<string>();
            for (int i = 0; i < sources.Count; i++)
            {
                Source source = sources[i];
                if (source.SurfaceOnly)
                {
                    source.WallSlot = -1;
                    source.SurfaceSlot = 0;
                }
                else
                {
                    DetectSlots(source.Renderer, source.Mesh, out source.WallSlot, out source.SurfaceSlot);
                    ResolveMaskPaths(ref source);
                    if (string.IsNullOrEmpty(source.WallMaskPath))
                        missingMasks.Add(source.Mesh.name + " wall ink");
                    if (string.IsNullOrEmpty(source.SurfaceMaskPath))
                        missingMasks.Add(source.Mesh.name + " surface fade");
                }
                sources[i] = source;
            }
            if (missingMasks.Count > 0)
            {
                EditorUtility.DisplayDialog("Missing modular masks",
                    "Bake these source masks before conversion:\n\n" + string.Join("\n", missingMasks), "OK");
                return;
            }
            string[] existingMeshes = AssetDatabase.FindAssets("t:Mesh", new[] { Root + "/Meshes" });
            var existingCanonicalNames = new HashSet<string>();
            for (int i = 0; i < existingMeshes.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(existingMeshes[i]);
                existingCanonicalNames.Add(CanonicalMeshName(Path.GetFileNameWithoutExtension(path)));
            }
            var selectedCanonicalNames = new HashSet<string>();
            for (int i = 0; i < sources.Count; i++)
                selectedCanonicalNames.Add(CanonicalMeshName(sources[i].Mesh.name));
            existingCanonicalNames.ExceptWith(selectedCanonicalNames);
            if (existingCanonicalNames.Count > 0)
            {
                EditorUtility.DisplayDialog("Modular Converter",
                    "The existing atlas contains modular pieces that are not represented by the current selection:\n\n" +
                    string.Join("\n", existingCanonicalNames) +
                    "\n\nSelect the complete pivot-fixed source set before regenerating.", "OK");
                return;
            }
            int columns = Mathf.CeilToInt(Mathf.Sqrt(sources.Count));
            int rows = Mathf.CeilToInt((float)sources.Count / columns);
            int wallRegionWidth = columns * _wallTileSize;
            int width = Mathf.NextPowerOfTwo(columns * (_wallTileSize + _surfaceTileSize));
            int height = Mathf.NextPowerOfTwo(rows * Mathf.Max(_wallTileSize, _surfaceTileSize));
            var atlasPixels = WhitePixels(width * height);

            for (int i = 0; i < sources.Count; i++)
            {
                Source source = sources[i];
                source.WallAtlasRect = MakeRect(i, columns, _wallTileSize, 0, width, height);
                source.SurfaceAtlasRect = MakeRect(i, columns, _surfaceTileSize, wallRegionWidth, width, height);
                if (!source.SurfaceOnly)
                {
                    BlitMask(source.WallMaskPath, atlasPixels, width,
                        i % columns * _wallTileSize, i / columns * _wallTileSize, _wallTileSize);
                    BlitMask(source.SurfaceMaskPath, atlasPixels, width,
                        wallRegionWidth + i % columns * _surfaceTileSize, i / columns * _surfaceTileSize, _surfaceTileSize);
                }
                sources[i] = source;
            }

            Texture2D atlas = SaveAtlas(atlasPixels, width, height);
            Material sharedMaterial = CreateSharedMaterial(atlas, sources);
            if (rebuildMeshesAndPrefabs)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Converting modular level", sources[i].Renderer.name, (float)i / sources.Count);
                    Mesh mesh = CreateMergedMesh(sources[i]);
                    SavePrefab(sources[i], mesh, sharedMaterial);
                }
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = sharedMaterial;
            Debug.Log(rebuildMeshesAndPrefabs
                ? $"Converted {sources.Count} modular objects into '{Root}'."
                : $"Regenerated the modular mask atlas from {sources.Count} source objects without changing meshes or prefabs.",
                sharedMaterial);
        }

        private Mesh CreateMergedMesh(Source source)
        {
            Mesh sourceMesh = source.Mesh;
            Mesh input = sourceMesh;
            bool ownsInput = false;
            if (sourceMesh.uv2 == null || sourceMesh.uv2.Length != sourceMesh.vertexCount)
            {
                input = Instantiate(sourceMesh);
                input.name = sourceMesh.name;
                Unwrapping.GenerateSecondaryUVSet(input);
                ownsInput = true;
            }
            Vector3[] positions = input.vertices;
            Vector3[] normals = input.normals;
            Vector4[] tangents = input.tangents;
            Vector2[] uv0 = input.uv;
            Vector2[] oldUv2 = input.uv2;
            Color[] sourceColors = input.colors;
            Bounds inputBounds = input.bounds;
            float inverseHeight = 1f / Mathf.Max(inputBounds.size.y, 0.0001f);
            var outPositions = new List<Vector3>(); var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>(); var outUv0 = new List<Vector2>();
            var outUv3 = new List<Vector2>(); var colors = new List<Color>(); var triangles = new List<int>();
            if (source.WallSlot >= 0)
                AppendSubmesh(source.WallSlot, 0f);
            AppendSubmesh(source.SurfaceSlot, 1f);

            void AppendSubmesh(int slot, float surface)
            {
                int[] indices = input.GetTriangles(slot);
                // Reuse the source mesh's indexed topology. The previous converter emitted a
                // unique vertex for every triangle corner, which disconnected every polygon and
                // allowed vertex displacement to open cracks throughout otherwise connected walls.
                var remappedVertices = new Dictionary<int, int>();
                for (int i = 0; i < indices.Length; i++)
                {
                    int index = indices[i];
                    if (remappedVertices.TryGetValue(index, out int existingIndex))
                    {
                        triangles.Add(existingIndex);
                        continue;
                    }

                    int outputIndex = outPositions.Count;
                    remappedVertices.Add(index, outputIndex);
                    outPositions.Add(positions[index]);
                    outNormals.Add(normals.Length == positions.Length ? normals[index] : Vector3.up);
                    outTangents.Add(tangents.Length == positions.Length ? tangents[index] : new Vector4(1, 0, 0, 1));
                    outUv0.Add(uv0.Length == positions.Length ? uv0[index] : Vector2.zero);
                    Vector2 maskUv = oldUv2.Length == positions.Length ? oldUv2[index] : Vector2.zero;
                    Rect atlasRect = surface > 0.5f ? source.SurfaceAtlasRect : source.WallAtlasRect;
                    outUv3.Add(new Vector2(atlasRect.x + maskUv.x * atlasRect.width,
                        atlasRect.y + maskUv.y * atlasRect.height));
                    Color sourceColor = sourceColors.Length == positions.Length ? sourceColors[index] : Color.white;
                    float normalizedHeight = Mathf.Clamp01((positions[index].y - inputBounds.min.y) * inverseHeight);
                    // R identifies surface/wall, G preserves vertex AO, and B stores normalized
                    // local mesh height for scale-independent bottom-only wall displacement.
                    colors.Add(new Color(surface, sourceColor.g, normalizedHeight, sourceColor.a));
                    triangles.Add(outputIndex);
                }
            }

            string path = $"{Root}/Meshes/{Safe(input.name)}_Modular.asset";
            Mesh output = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNewAsset = output == null;
            if (isNewAsset)
                output = new Mesh();
            else
                output.Clear(false);

            output.name = input.name + "_Modular";
            output.indexFormat = outPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            output.SetVertices(outPositions); output.SetNormals(outNormals); output.SetTangents(outTangents);
            output.SetUVs(0, outUv0); output.SetUVs(2, outUv3); output.SetColors(colors);
            output.SetTriangles(triangles, 0); output.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(output); // UV2 for lightmaps; UV3 remains the mask atlas.
            if (output.uv3 == null || output.uv3.Length != output.vertexCount)
                throw new InvalidOperationException($"UV3 style coordinates were lost while generating UV2 for '{input.name}'.");
            if (isNewAsset)
                AssetDatabase.CreateAsset(output, path);
            else
                EditorUtility.SetDirty(output);
            if (ownsInput)
                DestroyImmediate(input);
            return output;
        }

        private static Rect MakeRect(int index, int columns, int tileSize, int xOffset, int atlasWidth, int atlasHeight)
        {
            return new Rect(
                (float)(xOffset + index % columns * tileSize) / atlasWidth,
                (float)(index / columns * tileSize) / atlasHeight,
                (float)tileSize / atlasWidth,
                (float)tileSize / atlasHeight);
        }

        private void BlitMask(string assetPath, Color32[] atlas, int atlasWidth, int x0, int y0, int tileSize)
        {
            Color32[] mask = LoadAndResize(assetPath, tileSize);
            for (int y = 0; y < tileSize; y++) for (int x = 0; x < tileSize; x++)
                atlas[(y0 + y) * atlasWidth + x0 + x] = mask[y * tileSize + x];
        }

        private static Color32[] LoadAndResize(string assetPath, int tileSize)
        {
            if (!File.Exists(Path.GetFullPath(assetPath))) return WhitePixels(tileSize * tileSize);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.LoadImage(File.ReadAllBytes(Path.GetFullPath(assetPath)));
            var result = new Color32[tileSize * tileSize];
            for (int y = 0; y < tileSize; y++) for (int x = 0; x < tileSize; x++)
                result[y * tileSize + x] = source.GetPixelBilinear((x + .5f) / tileSize, (y + .5f) / tileSize);
            DestroyImmediate(source); return result;
        }

        private Texture2D SaveAtlas(Color32[] pixels, int width, int height)
        {
            const string path = Root + "/Textures/ModularLevel_MaskAtlas.png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG()); DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.mipmapEnabled = true;
            // TextureImporter defaults can downscale a correctly generated 4K/8K atlas,
            // making every tile blurry. Preserve the actual generated atlas dimensions.
            importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, 16384);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private Material CreateSharedMaterial(Texture2D atlas, List<Source> sources)
        {
            const string path = Root + "/Materials/ModularLevel_Shared.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Project/Mobile Toon Modular Level");
            bool isNewMaterial = material == null;
            if (isNewMaterial) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); } else material.shader = shader;
            Material wallSource = null;
            Material surfaceSource = null;
            for (int i = 0; i < sources.Count; i++)
            {
                Material[] originals = sources[i].Renderer.sharedMaterials;
                if (surfaceSource == null && sources[i].SurfaceSlot >= 0 && sources[i].SurfaceSlot < originals.Length)
                    surfaceSource = originals[sources[i].SurfaceSlot];
                if (wallSource == null && sources[i].WallSlot >= 0 && sources[i].WallSlot < originals.Length)
                    wallSource = originals[sources[i].WallSlot];
            }
            material.SetTexture("_WallMap", GetBaseTexture(wallSource));
            material.SetTexture("_SurfaceMap", GetBaseTexture(surfaceSource));
            // Initialize authored colors once. Subsequent atlas regeneration must preserve
            // color adjustments made directly on the shared material.
            if (isNewMaterial)
            {
                material.SetColor("_WallColor", GetBaseColor(wallSource));
                material.SetColor("_SurfaceColor", GetBaseColor(surfaceSource));
            }
            material.SetTexture("_StyleMask", atlas); EditorUtility.SetDirty(material); return material;
        }

        private static Texture GetBaseTexture(Material material) => material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material != null && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        private static Color GetBaseColor(Material material) => material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;

        private static void SavePrefab(Source source, Mesh mesh, Material material)
        {
            var go = new GameObject(source.Renderer.name + "_Modular");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = source.Renderer.shadowCastingMode; renderer.receiveShadows = source.Renderer.receiveShadows;
            PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{Safe(source.Renderer.name)}_Modular.prefab"); DestroyImmediate(go);
        }

        private static void DetectSlots(Renderer renderer, Mesh mesh, out int wall, out int surface)
        {
            if (TryDetectSlotsFromMaterialNames(renderer, out wall, out surface))
                return;

            const float minimumUpDot = 0.35f;
            Vector3[] v = mesh.vertices;
            float highestSurfaceY = float.MinValue;
            surface = -1;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = v[triangles[i]];
                    Vector3 b = v[triangles[i + 1]];
                    Vector3 c = v[triangles[i + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.sqrMagnitude <= 0.000001f ||
                        Vector3.Dot(cross.normalized, Vector3.up) < minimumUpDot)
                        continue;
                    float centroidY = (a.y + b.y + c.y) / 3f;
                    if (centroidY > highestSurfaceY)
                    {
                        highestSurfaceY = centroidY;
                        surface = s;
                    }
                }
            }
            if (surface < 0)
                surface = 0;
            wall = surface == 0 ? 1 : 0;
        }

        private static bool TryDetectSlotsFromMaterialNames(Renderer renderer, out int wall, out int surface)
        {
            wall = -1; surface = -1;
            if (renderer == null || renderer.sharedMaterials.Length != 2)
                return false;
            for (int i = 0; i < 2; i++)
            {
                string name = renderer.sharedMaterials[i] != null ? renderer.sharedMaterials[i].name.ToLowerInvariant() : string.Empty;
                bool isSurface = ContainsAny(name, "surface", "top", "ground", "snow", "grass");
                bool isWall = ContainsAny(name, "wall", "rock", "cliff", "side") ||
                              (!isSurface && name.Contains("edge"));
                if (isSurface) surface = i;
                if (isWall) wall = i;
            }
            if (surface >= 0 && wall < 0) wall = 1 - surface;
            if (wall >= 0 && surface < 0) surface = 1 - wall;
            return wall >= 0 && surface >= 0 && wall != surface;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++) if (value.Contains(terms[i])) return true;
            return false;
        }

        private static List<Source> CollectSources()
        {
            var result = new List<Source>(); var seenMeshes = new HashSet<Mesh>();
            foreach (GameObject root in Selection.gameObjects) foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !seenMeshes.Add(filter.sharedMesh))
                    continue;
                string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh).Replace('\\', '/');
                if (meshPath.StartsWith(Root + "/Meshes/", StringComparison.OrdinalIgnoreCase))
                    continue; // Generated modular outputs are never valid stage-2 sources.
                int materialCount = renderer.sharedMaterials.Length;
                int subMeshCount = filter.sharedMesh.subMeshCount;
                if ((materialCount == 1 && subMeshCount == 1) || (materialCount == 2 && subMeshCount == 2))
                    result.Add(new Source { Renderer = renderer, Mesh = filter.sharedMesh, SurfaceOnly = materialCount == 1 });
            }
            return result;
        }

        private static string SourceKey(Mesh mesh) => AssetDatabase.GetAssetPath(mesh) + ":" + mesh.name;
        private static string CanonicalMeshName(string meshName)
        {
            const string suffix = "_Modular";
            while (meshName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                meshName = meshName.Substring(0, meshName.Length - suffix.Length);
            return meshName;
        }

        private static string GetMaskBaseName(string meshName)
        {
            const string modularSuffix = "_Modular";
            if (meshName.EndsWith(modularSuffix, StringComparison.OrdinalIgnoreCase))
                meshName = meshName.Substring(0, meshName.Length - modularSuffix.Length);

            // Prefer masks baked from the final pivot-fixed mesh because their UVs are the
            // authoritative ones. Older pre-pivot masks remain a valid fallback.
            if (MaskPairExists(meshName))
                return meshName;

            const string pivotSuffix = "_PivotFixed";
            if (meshName.EndsWith(pivotSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string originalName = meshName.Substring(0, meshName.Length - pivotSuffix.Length);
                if (MaskPairExists(originalName))
                    return originalName;
            }
            return meshName;
        }

        private static bool MaskPairExists(string baseName) =>
            File.Exists(Path.GetFullPath($"{SourceMasks}/{baseName}_WallInkMask.png")) &&
            File.Exists(Path.GetFullPath($"{SourceMasks}/{baseName}_SurfaceEdgeFade.png"));

        private static void ResolveMaskPaths(ref Source source)
        {
            Material[] materials = source.Renderer.sharedMaterials;
            source.WallMaskPath = GetMaterialTexturePath(materials, source.WallSlot, "_InkMask");
            source.SurfaceMaskPath = GetMaterialTexturePath(materials, source.SurfaceSlot, "_SurfaceEdgeMask");

            string maskName = GetMaskBaseName(source.Mesh.name);
            if (string.IsNullOrEmpty(source.WallMaskPath))
                source.WallMaskPath = ExistingPath($"{SourceMasks}/{maskName}_WallInkMask.png");
            if (string.IsNullOrEmpty(source.SurfaceMaskPath))
                source.SurfaceMaskPath = ExistingPath($"{SourceMasks}/{maskName}_SurfaceEdgeFade.png");
        }

        private static string GetMaterialTexturePath(Material[] materials, int slot, string property)
        {
            if (slot < 0 || slot >= materials.Length || materials[slot] == null ||
                !materials[slot].HasProperty(property))
                return null;
            Texture texture = materials[slot].GetTexture(property);
            string path = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
            return ExistingPath(path);
        }

        private static string ExistingPath(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) && File.Exists(Path.GetFullPath(assetPath)) ? assetPath : null;

        private static Color32[] WhitePixels(int count) { var p = new Color32[count]; Array.Fill(p, new Color32(255,255,255,255)); return p; }
        private static string Safe(string value) { foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
        private static void EnsureFolders() { Ensure(Root); Ensure(Root+"/Meshes"); Ensure(Root+"/Prefabs"); Ensure(Root+"/Textures"); Ensure(Root+"/Materials"); }
        private static void Ensure(string path) { string parent = Path.GetDirectoryName(path)?.Replace('\\','/'); if (!AssetDatabase.IsValidFolder(path) && !string.IsNullOrEmpty(parent)) { if (!AssetDatabase.IsValidFolder(parent)) Ensure(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); } }

        private struct Source
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public int WallSlot;
            public int SurfaceSlot;
            public bool SurfaceOnly;
            public Rect WallAtlasRect;
            public Rect SurfaceAtlasRect;
            public string WallMaskPath;
            public string SurfaceMaskPath;
        }
    }
}
