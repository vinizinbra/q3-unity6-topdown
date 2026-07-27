using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuantumUser.Editor
{
    /// <summary>
    /// Bakes hard mesh edges into UV2 as a texture. The resulting mask is intended
    /// to be multiplied with a toon material, so white means no ink and black means ink.
    /// </summary>
    public sealed class Uv2InkMaskBakerWindow : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/_Project/Art/InkTextures";

        [SerializeField] private Mesh _mesh;
        [SerializeField] private Renderer _sourceRenderer;
        [SerializeField] private int _textureSize = 512;
        [SerializeField] private float _creaseAngle = 35f;
        [SerializeField] private int _lineWidth = 5;
        [SerializeField] private int _lineFadeWidth = 8;
        [SerializeField] private float _lineFadePower = 1.5f;
        [SerializeField] private bool _autoDetectMaterialSlots = true;
        [SerializeField] private int _wallSubMesh;
        [SerializeField] private int _surfaceSubMesh = 1;
        [SerializeField] private int _surfaceFadeWidth = 64;
        [SerializeField] private float _surfaceFadeStrength = 0.65f;
        [SerializeField] private Color _surfaceEdgeColor = new Color(0.22f, 0.16f, 0.10f, 1f);
        private bool _isBulkBake;

        [MenuItem("Tools/Art/UV2 Ink Mask Baker")]
        private static void Open()
        {
            var window = GetWindow<Uv2InkMaskBakerWindow>("UV2 Ink Baker");
            window.minSize = new Vector2(430f, 390f);
            window.TryUseSelection();
        }

        [MenuItem("Tools/Art/UV2 Ink Mask Baker", true)]
        private static bool ValidateOpen() => !Application.isPlaying;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Select a Mesh asset or a GameObject with a MeshFilter/SkinnedMeshRenderer. " +
                "Slot 0 defaults to rock wall and slot 1 to surface. The tool bakes wall strokes, " +
                "a surface fade at shared edges, creates two materials, and assigns them when a Renderer is selected. " +
                "The mesh needs non-overlapping UV2 coordinates and Read/Write enabled.",
                MessageType.Info);

            _mesh = (Mesh)EditorGUILayout.ObjectField("Mesh", _mesh, typeof(Mesh), false);
            _sourceRenderer = (Renderer)EditorGUILayout.ObjectField("Source Renderer", _sourceRenderer, typeof(Renderer), true);
            _textureSize = EditorGUILayout.IntPopup("Texture Size", _textureSize,
                new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
            int maxSubMesh = Mathf.Max(0, (_mesh != null ? _mesh.subMeshCount : 1) - 1);
            _autoDetectMaterialSlots = EditorGUILayout.Toggle(
                new GUIContent("Auto Detect Material Slots", "The highest upward-facing triangle identifies the top-surface material. Lower horizontal ledges are ignored."),
                _autoDetectMaterialSlots);
            using (new EditorGUI.DisabledScope(_autoDetectMaterialSlots))
            {
                _wallSubMesh = EditorGUILayout.IntSlider("Rock Wall Material Slot", _wallSubMesh, 0, maxSubMesh);
                _surfaceSubMesh = EditorGUILayout.IntSlider("Surface Material Slot", _surfaceSubMesh, 0, maxSubMesh);
            }
            _creaseAngle = EditorGUILayout.Slider("Crease Angle", _creaseAngle, 0f, 180f);
            _lineWidth = EditorGUILayout.IntSlider("Stroke Core (pixels)", _lineWidth, 1, 32);
            _lineFadeWidth = EditorGUILayout.IntSlider("Stroke Outer Fade (pixels)", _lineFadeWidth, 0, 64);
            _lineFadePower = EditorGUILayout.Slider(
                new GUIContent("Stroke Fade Curve", "Higher values make the outer glow fall off faster."),
                _lineFadePower, 0.25f, 4f);
            _surfaceFadeWidth = EditorGUILayout.IntSlider("Surface Fade (pixels)", _surfaceFadeWidth, 1, 256);
            _surfaceFadeStrength = EditorGUILayout.Slider("Surface Fade Strength", _surfaceFadeStrength, 0f, 1f);
            _surfaceEdgeColor = EditorGUILayout.ColorField("Surface Edge Color", _surfaceEdgeColor);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_mesh == null))
            {
                if (GUILayout.Button("Bake Current Object", GUILayout.Height(30f)))
                    Bake();
            }

            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button("Bake All Selected Objects", GUILayout.Height(34f)))
                    BakeSelectedObjects();
            }
        }

        private void OnSelectionChange()
        {
            TryUseSelection();
            Repaint();
        }

        private void TryUseSelection()
        {
            if (Selection.activeObject is Mesh selectedMesh)
            {
                _mesh = selectedMesh;
                _sourceRenderer = null;
                return;
            }

            if (Selection.activeGameObject == null)
                return;

            var meshFilter = Selection.activeGameObject.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                _mesh = meshFilter.sharedMesh;
                _sourceRenderer = meshFilter.GetComponent<Renderer>();
                return;
            }

            var skinned = Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                _mesh = skinned.sharedMesh;
                _sourceRenderer = skinned;
            }
        }

        private void Bake()
        {
            Vector3[] vertices;
            Vector2[] uv2;
            try
            {
                vertices = _mesh.vertices;
                uv2 = _mesh.uv2;
            }
            catch (UnityException exception)
            {
                EditorUtility.DisplayDialog("UV2 Ink Baker",
                    "The mesh data cannot be read. Enable Read/Write in its Model Import Settings.\n\n" +
                    exception.Message, "OK");
                return;
            }

            if (uv2 == null || uv2.Length != vertices.Length)
            {
                EditorUtility.DisplayDialog("UV2 Ink Baker",
                    "This mesh does not contain a complete UV2 channel. Generate lightmap UVs or provide UV2 first.", "OK");
                return;
            }

            if (_autoDetectMaterialSlots && _mesh.subMeshCount >= 2)
                DetectMaterialSlots(vertices);

            if (_mesh.subMeshCount < 2 || _wallSubMesh == _surfaceSubMesh)
            {
                EditorUtility.DisplayDialog("UV2 Ink Baker",
                    "Choose two different material slots: one rock wall and one surface.", "OK");
                return;
            }

            EnsureAssetFolderExists(DefaultOutputFolder);
            var edgeMap = BuildEdgeMap(vertices);
            var wallPixels = CreateWhitePixels();
            var surfacePixels = CreateWhitePixels();

            int wallEdges = 0;
            int sharedEdges = 0;
            foreach (var pair in edgeMap)
            {
                EdgeInfo edge = pair.Value;
                if (HasCrease(edge) && HasFaceInSubMesh(edge, _wallSubMesh))
                {
                    DrawFacesForSubMesh(wallPixels, uv2, edge, _wallSubMesh, false);
                    wallEdges++;
                }

                if (HasFaceInSubMesh(edge, _wallSubMesh) && HasFaceInSubMesh(edge, _surfaceSubMesh))
                {
                    DrawFacesForSubMesh(surfacePixels, uv2, edge, _surfaceSubMesh, true);
                    sharedEdges++;
                }
            }

            string safeName = MakeSafeFileName(_mesh.name);
            string wallTexturePath = $"{DefaultOutputFolder}/{safeName}_WallInkMask.png";
            string surfaceTexturePath = $"{DefaultOutputFolder}/{safeName}_SurfaceEdgeFade.png";
            Texture2D wallTexture = WriteTexture(wallTexturePath, wallPixels);
            Texture2D surfaceTexture = WriteTexture(surfaceTexturePath, surfacePixels);
            Material wallMaterial = CreateMaterial(safeName + "_Wall", _wallSubMesh, wallTexture, null, false);
            Material surfaceMaterial = CreateMaterial(safeName + "_Surface", _surfaceSubMesh, null, surfaceTexture, true);
            AssignMaterials(wallMaterial, surfaceMaterial);

            AssetDatabase.SaveAssets();
            if (!_isBulkBake)
            {
                Selection.activeObject = surfaceMaterial;
                EditorGUIUtility.PingObject(surfaceMaterial);
            }
            Debug.Log($"Generated wall ink ({wallEdges} creases), surface fade ({sharedEdges} shared edges), " +
                      $"and two materials for '{_mesh.name}' in '{DefaultOutputFolder}'. " +
                      $"Wall slot: {_wallSubMesh}, surface slot: {_surfaceSubMesh}.", surfaceMaterial);
        }

        private void BakeSelectedObjects()
        {
            UnityEngine.Object[] originalSelection = Selection.objects;
            List<RendererMeshPair> targets = CollectSelectedMeshes();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("UV2 Ink Baker",
                    "The selected objects do not contain any MeshRenderer or SkinnedMeshRenderer meshes.", "OK");
                return;
            }

            Mesh previousMesh = _mesh;
            Renderer previousRenderer = _sourceRenderer;
            _isBulkBake = true;
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    RendererMeshPair target = targets[i];
                    EditorUtility.DisplayProgressBar("Baking environment materials",
                        $"{target.Renderer.name} ({i + 1}/{targets.Count})", (float)i / targets.Count);
                    _mesh = target.Mesh;
                    _sourceRenderer = target.Renderer;
                    Bake();
                }
            }
            finally
            {
                _isBulkBake = false;
                _mesh = previousMesh;
                _sourceRenderer = previousRenderer;
                EditorUtility.ClearProgressBar();
                Selection.objects = originalSelection;
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"UV2 Ink Baker processed {targets.Count} selected renderer(s).", this);
        }

        private static List<RendererMeshPair> CollectSelectedMeshes()
        {
            var targets = new List<RendererMeshPair>();
            var seen = new HashSet<Renderer>();
            GameObject[] selectedObjects = Selection.gameObjects;
            for (int selectedIndex = 0; selectedIndex < selectedObjects.Length; selectedIndex++)
            {
                Renderer[] renderers = selectedObjects[selectedIndex].GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (!seen.Add(renderer))
                        continue;

                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned)
                        mesh = skinned.sharedMesh;
                    else if (renderer is MeshRenderer)
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null)
                            mesh = filter.sharedMesh;
                    }

                    if (mesh != null && mesh.subMeshCount == 2 && renderer.sharedMaterials.Length == 2)
                        targets.Add(new RendererMeshPair(renderer, mesh));
                }
            }
            return targets;
        }

        private bool HasCrease(EdgeInfo edge)
        {
            for (int first = 0; first < edge.Faces.Count - 1; first++)
            for (int second = first + 1; second < edge.Faces.Count; second++)
            {
                if (Vector3.Angle(edge.Faces[first].Normal, edge.Faces[second].Normal) >= _creaseAngle)
                    return true;
            }

            return false;
        }

        private Color32[] CreateWhitePixels()
        {
            var pixels = new Color32[_textureSize * _textureSize];
            Array.Fill(pixels, new Color32(255, 255, 255, 255));
            return pixels;
        }

        private static bool HasFaceInSubMesh(EdgeInfo edge, int subMesh)
        {
            for (int i = 0; i < edge.Faces.Count; i++)
                if (edge.Faces[i].SubMesh == subMesh)
                    return true;
            return false;
        }

        private void DrawFacesForSubMesh(Color32[] pixels, Vector2[] uv2, EdgeInfo edge, int subMesh, bool fade)
        {
            for (int i = 0; i < edge.Faces.Count; i++)
            {
                EdgeFace face = edge.Faces[i];
                if (face.SubMesh != subMesh)
                    continue;
                Vector2 a = uv2[face.VertexA];
                Vector2 b = uv2[face.VertexB];
                if (!IsInsideUvTile(a) || !IsInsideUvTile(b))
                    continue;
                if (fade) DrawFadeLine(pixels, a, b);
                else DrawLine(pixels, a, b);
            }
        }

        private void DetectMaterialSlots(Vector3[] vertices)
        {
            int count = _mesh.subMeshCount;
            if (TryDetectSlotsFromMaterialNames(out int namedWall, out int namedSurface))
            {
                _wallSubMesh = namedWall;
                _surfaceSubMesh = namedSurface;
                return;
            }

            const float minimumUpDot = 0.35f;
            float highestSurfaceY = float.MinValue;
            _surfaceSubMesh = -1;

            for (int subMesh = 0; subMesh < count; subMesh++)
            {
                int[] triangles = _mesh.GetTriangles(subMesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]];
                    Vector3 b = vertices[triangles[i + 1]];
                    Vector3 c = vertices[triangles[i + 2]];
                    Vector3 cross = Vector3.Cross(
                        b - a,
                        c - a);
                    if (cross.sqrMagnitude <= 0.000001f)
                        continue;
                    if (Vector3.Dot(cross.normalized, Vector3.up) < minimumUpDot)
                        continue;
                    float centroidY = (a.y + b.y + c.y) / 3f;
                    if (centroidY > highestSurfaceY)
                    {
                        highestSurfaceY = centroidY;
                        _surfaceSubMesh = subMesh;
                    }
                }
            }

            if (_surfaceSubMesh < 0)
                _surfaceSubMesh = 0;
            _wallSubMesh = _surfaceSubMesh == 0 ? 1 : 0;
        }

        private bool TryDetectSlotsFromMaterialNames(out int wall, out int surface)
        {
            wall = -1;
            surface = -1;
            if (_sourceRenderer == null || _sourceRenderer.sharedMaterials.Length != 2)
                return false;
            for (int i = 0; i < 2; i++)
            {
                string materialName = _sourceRenderer.sharedMaterials[i] != null
                    ? _sourceRenderer.sharedMaterials[i].name.ToLowerInvariant()
                    : string.Empty;
                bool isSurface = ContainsAny(materialName, "surface", "top", "ground", "snow", "grass");
                bool isWall = ContainsAny(materialName, "wall", "rock", "cliff", "side") ||
                              (!isSurface && materialName.Contains("edge"));
                if (isSurface)
                    surface = i;
                if (isWall)
                    wall = i;
            }
            if (surface >= 0 && wall < 0) wall = 1 - surface;
            if (wall >= 0 && surface < 0) surface = 1 - wall;
            return wall >= 0 && surface >= 0 && wall != surface;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
                if (value.Contains(terms[i])) return true;
            return false;
        }

        private Dictionary<GeometricEdgeKey, EdgeInfo> BuildEdgeMap(Vector3[] vertices)
        {
            var edges = new Dictionary<GeometricEdgeKey, EdgeInfo>();
            for (int subMesh = 0; subMesh < _mesh.subMeshCount; subMesh++)
            {
                int[] triangles = _mesh.GetTriangles(subMesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                    AddEdge(edges, vertices, a, b, normal, subMesh);
                    AddEdge(edges, vertices, b, c, normal, subMesh);
                    AddEdge(edges, vertices, c, a, normal, subMesh);
                }
            }
            return edges;
        }

        private static void AddEdge(
            Dictionary<GeometricEdgeKey, EdgeInfo> edges,
            Vector3[] vertices,
            int a,
            int b,
            Vector3 normal,
            int subMesh)
        {
            var key = new GeometricEdgeKey(vertices[a], vertices[b]);
            if (!edges.TryGetValue(key, out EdgeInfo info))
            {
                info = new EdgeInfo();
                edges.Add(key, info);
            }

            info.Faces.Add(new EdgeFace(a, b, normal, subMesh));
        }

        private void DrawLine(Color32[] pixels, Vector2 uvA, Vector2 uvB)
        {
            Vector2 a = new Vector2(uvA.x * (_textureSize - 1), uvA.y * (_textureSize - 1));
            Vector2 b = new Vector2(uvB.x * (_textureSize - 1), uvB.y * (_textureSize - 1));
            float coreRadius = Mathf.Max(0.5f, _lineWidth * 0.5f);
            float outerRadius = coreRadius + Mathf.Max(0, _lineFadeWidth);
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - outerRadius), 0, _textureSize - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + outerRadius), 0, _textureSize - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - outerRadius), 0, _textureSize - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + outerRadius), 0, _textureSize - 1);

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, b);
                float coreCoverage = Mathf.Clamp01(coreRadius + 0.5f - distance);
                float fadeCoverage = 0f;
                if (_lineFadeWidth > 0 && distance < outerRadius)
                {
                    float fadeT = Mathf.Clamp01((outerRadius - distance) / Mathf.Max(1f, _lineFadeWidth));
                    fadeCoverage = Mathf.Pow(fadeT, Mathf.Max(0.01f, _lineFadePower));
                }
                float glowCoverage = Mathf.Max(coreCoverage, fadeCoverage);
                if (glowCoverage <= 0f)
                    continue;

                int index = y * _textureSize + x;
                Color32 current = pixels[index];
                byte coreValue = (byte)Mathf.RoundToInt(255f * (1f - coreCoverage));
                byte glowValue = (byte)Mathf.RoundToInt(255f * (1f - glowCoverage));
                current.r = Math.Min(current.r, coreValue);
                current.g = Math.Min(current.g, glowValue);
                current.b = current.r;
                pixels[index] = current;
            }
        }

        private void DrawFadeLine(Color32[] pixels, Vector2 uvA, Vector2 uvB)
        {
            Vector2 a = new Vector2(uvA.x * (_textureSize - 1), uvA.y * (_textureSize - 1));
            Vector2 b = new Vector2(uvB.x * (_textureSize - 1), uvB.y * (_textureSize - 1));
            float radius = Mathf.Max(1f, _surfaceFadeWidth);
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius), 0, _textureSize - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius), 0, _textureSize - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius), 0, _textureSize - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius), 0, _textureSize - 1);

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, b);
                if (distance > radius)
                    continue;
                float normalized = Mathf.Clamp01(distance / radius);
                float smoothFade = normalized * normalized * (3f - 2f * normalized);
                byte value = (byte)Mathf.RoundToInt(smoothFade * 255f);
                int index = y * _textureSize + x;
                if (value < pixels[index].r)
                    pixels[index] = new Color32(value, value, value, 255);
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.000001f)
                return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSquared);
            return Vector2.Distance(point, a + segment * t);
        }

        private static bool IsInsideUvTile(Vector2 uv) =>
            uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        private Texture2D WriteTexture(string path, Color32[] pixels)
        {
            var texture = new Texture2D(_textureSize, _textureSize, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private Material CreateMaterial(
            string materialName,
            int sourceSlot,
            Texture2D inkMask,
            Texture2D surfaceMask,
            bool isSurface)
        {
            string path = $"{DefaultOutputFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool createdMaterial = material == null;
            Shader shader = Shader.Find("Project/Mobile Toon Environment UV2 Ink");
            if (shader == null)
                throw new InvalidOperationException("Could not find Project/Mobile Toon Environment UV2 Ink shader.");

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            CopyBasePropertiesFromSource(material, sourceSlot);
            material.SetTexture("_InkMask", inkMask);
            material.SetFloat("_InkStrength", isSurface ? 0f : 1f);
            if (!isSurface && createdMaterial)
            {
                material.SetFloat("_InkOuterGlowStrength", 0.3f);
            }
            material.SetTexture("_SurfaceEdgeMask", surfaceMask);
            material.SetColor("_SurfaceEdgeColor", _surfaceEdgeColor);
            material.SetFloat("_SurfaceEdgeStrength", isSurface ? _surfaceFadeStrength : 0f);
            material.SetFloat("_AOStrength", isSurface ? 0f : 0.45f);
            material.SetFloat("_FresnelStrength", isSurface ? 0f : 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private void CopyBasePropertiesFromSource(Material target, int sourceSlot)
        {
            if (_sourceRenderer == null || sourceSlot >= _sourceRenderer.sharedMaterials.Length)
                return;
            Material source = _sourceRenderer.sharedMaterials[sourceSlot];
            if (source == null)
                return;

            Texture baseTexture = source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            if (baseTexture != null)
                target.SetTexture("_BaseMap", baseTexture);
            if (source.HasProperty("_BaseColor"))
                target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            else if (source.HasProperty("_Color"))
                target.SetColor("_BaseColor", source.GetColor("_Color"));
        }

        private void AssignMaterials(Material wallMaterial, Material surfaceMaterial)
        {
            if (_sourceRenderer == null)
                return;
            Material[] materials = _sourceRenderer.sharedMaterials;
            if (_wallSubMesh >= materials.Length || _surfaceSubMesh >= materials.Length)
                return;
            Undo.RecordObject(_sourceRenderer, "Assign generated toon materials");
            materials[_wallSubMesh] = wallMaterial;
            materials[_surfaceSubMesh] = surfaceMaterial;
            _sourceRenderer.sharedMaterials = materials;
            EditorUtility.SetDirty(_sourceRenderer);
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static void ConfigureImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        private static void EnsureAssetFolderExists(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string currentPath = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                currentPath = nextPath;
            }
        }

        private readonly struct QuantizedPosition : IEquatable<QuantizedPosition>, IComparable<QuantizedPosition>
        {
            // Imported vertices split by normals/UVs normally retain identical positions.
            // Quantizing also tolerates tiny importer precision differences.
            private const float PositionScale = 100000f;
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            public QuantizedPosition(Vector3 position)
            {
                _x = Mathf.RoundToInt(position.x * PositionScale);
                _y = Mathf.RoundToInt(position.y * PositionScale);
                _z = Mathf.RoundToInt(position.z * PositionScale);
            }

            public int CompareTo(QuantizedPosition other)
            {
                int result = _x.CompareTo(other._x);
                if (result != 0) return result;
                result = _y.CompareTo(other._y);
                return result != 0 ? result : _z.CompareTo(other._z);
            }

            public bool Equals(QuantizedPosition other) => _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object obj) => obj is QuantizedPosition other && Equals(other);
            public override int GetHashCode() => unchecked((((_x * 397) ^ _y) * 397) ^ _z);
        }

        private readonly struct GeometricEdgeKey : IEquatable<GeometricEdgeKey>
        {
            private readonly QuantizedPosition _a;
            private readonly QuantizedPosition _b;

            public GeometricEdgeKey(Vector3 a, Vector3 b)
            {
                var positionA = new QuantizedPosition(a);
                var positionB = new QuantizedPosition(b);
                if (positionA.CompareTo(positionB) <= 0)
                {
                    _a = positionA;
                    _b = positionB;
                }
                else
                {
                    _a = positionB;
                    _b = positionA;
                }
            }

            public bool Equals(GeometricEdgeKey other) => _a.Equals(other._a) && _b.Equals(other._b);
            public override bool Equals(object obj) => obj is GeometricEdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked((_a.GetHashCode() * 397) ^ _b.GetHashCode());
        }

        private readonly struct EdgeFace
        {
            public readonly int VertexA;
            public readonly int VertexB;
            public readonly Vector3 Normal;
            public readonly int SubMesh;

            public EdgeFace(int vertexA, int vertexB, Vector3 normal, int subMesh)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                Normal = normal;
                SubMesh = subMesh;
            }
        }

        private sealed class EdgeInfo
        {
            public readonly List<EdgeFace> Faces = new List<EdgeFace>(2);
            public int FaceCount => Faces.Count;
        }

        private readonly struct RendererMeshPair
        {
            public readonly Renderer Renderer;
            public readonly Mesh Mesh;

            public RendererMeshPair(Renderer renderer, Mesh mesh)
            {
                Renderer = renderer;
                Mesh = mesh;
            }
        }
    }
}
