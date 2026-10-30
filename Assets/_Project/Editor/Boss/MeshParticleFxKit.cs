namespace RiftRaiders.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// Shared pieces of the mesh-particle hit effects (Riff Riot's Power Chord, Dono do Céu's whistle shriek): one mesh
    /// particle per layer, shapes carried by vertex colours on feathered 90 deg arcs / a soft cone (local +Z = forward,
    /// radius 1 = particle size), plain Mobile/Particles materials, and a gameplay-camera preview of the cone.
    /// Each builder keeps its own look (layers, colours, timing); this only holds the plumbing they share.
    /// </summary>
    public static class MeshParticleFxKit
    {
        public const string AdditiveShader = "Mobile/Particles/Additive";
        public const string AlphaBlendedShader = "Mobile/Particles/Alpha Blended";

        // Vertex colours carry the shapes; the soft glow texture is sampled at its centre (UVs are all 0.5) - a persistent
        // white, since the built-in whiteTexture does not survive asset serialization.
        public static Material EnsureMaterial(string path, string shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find(shader);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3rd-party/Epic Toon FX/Textures/glow1.png");
            material.renderQueue = shader == AdditiveShader ? 3100 : 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        // Overwrites in place when the mesh asset exists, so references (and guids) survive rebuilds.
        public static Mesh SaveMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static Mesh BuildMesh(string name, List<Vector3> vertices, List<Color> colors, List<int> indices)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            // Mesh particles need packed UNorm8 colours (Float32 colours are misread by Unity 6).
            var packed = new Color32[colors.Count];
            for (int i = 0; i < colors.Count; i++)
                packed[i] = colors[i];
            mesh.colors32 = packed;
            mesh.SetTriangles(indices, 0);
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2(0.5f, 0.5f);
            mesh.uv = uv;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        public static void Quad(List<int> indices, int k, int stride)
        {
            indices.AddRange(new[] { k, k + stride, k + 1, k + 1, k + stride, k + stride + 1 });
        }

        // A 90 deg arc band of `thickness` ending at `outerRadius`: `across` is the alpha profile over its width (one row
        // each), and both ends feather over `edgeSegments` of its 96 segments so it never pokes past the cone.
        public static Mesh Arc(string name, float thickness, float outerRadius, float[] across, float edgeSegments)
        {
            const int segments = 96;
            int rows = across.Length;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-45f, 45f, i / (float)segments) * Mathf.Deg2Rad;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(i, segments - i) / edgeSegments);
                for (int j = 0; j < rows; j++)
                {
                    float r = outerRadius - thickness + thickness * j / (rows - 1);
                    vertices.Add(new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r));
                    colors.Add(new Color(1f, 1f, 1f, across[j] * edge));
                    if (i < segments && j < rows - 1)
                        Quad(indices, i * rows + j, rows);
                }
            }

            return BuildMesh(name, vertices, colors, indices);
        }

        // The whole 90 deg cone as one soft fill: alpha 1 at the apex to `farAlpha`, fading out from `fadeStart` to the rim.
        public static Mesh SoftCone(string name, float farAlpha, float fadeStart)
        {
            const int angles = 64, rings = 16;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();

            for (int a = 0; a <= angles; a++)
            {
                float angle = Mathf.Lerp(-45f, 45f, a / (float)angles) * Mathf.Deg2Rad;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(a, angles - a) / 4f);
                for (int r = 0; r <= rings; r++)
                {
                    float radius = r / (float)rings;
                    vertices.Add(new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
                    colors.Add(new Color(1f, 1f, 1f, Mathf.Lerp(1f, farAlpha, radius) * (1f - Mathf.SmoothStep(fadeStart, 1f, radius)) * edge));
                    if (a < angles && r < rings)
                        Quad(indices, a * (rings + 1) + r, rings + 1);
                }
            }

            return BuildMesh(name, vertices, colors, indices);
        }

        public static void ApplyMeshRenderer(ParticleSystem ps, Mesh mesh, Material material)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = material;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Gameplay-camera stills (FOV 14, rotation 45,0,0, no post processing/fog) of a cone effect over a flat floor of
        // `floorColor`, its 90 deg x `range` cone outlined in orange (and a 1 m grid when `meterGrid`), one PNG per entry of
        // `times` at outputPath(time). The camera sits at (0,36,-32.5 + cameraForward). `mustBeDeadAt` >= 0 also checks no
        // particle is still alive at that time.
        public static void RenderConePreview(string prefabPath, float range, Color floorColor, float cameraForward, float[] times,
            Func<float, string> outputPath, bool meterGrid = false, float mustBeDeadAt = -1f)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            bool fog = RenderSettings.fog;
            RenderTexture rt = null;
            Material floorMaterial = null, lineMaterial = null;

            try
            {
                RenderSettings.fog = false;
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.localScale = Vector3.one * 12f;
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = floorColor };
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

                var cameraObject = new GameObject("Gameplay Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(0f, 36f, -32.5f + cameraForward), Quaternion.Euler(45f, 0f, 0f));
                camera.fieldOfView = 14f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 120f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = floorColor;
                camera.scene = scene;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                camera.allowHDR = false;
                camera.allowMSAA = true;

                lineMaterial = new Material(Shader.Find(AlphaBlendedShader)) { mainTexture = Texture2D.whiteTexture };
                var outline = new List<Vector3> { new Vector3(0f, 0.012f, 0f) };
                for (int i = 0; i <= 90; i++)
                {
                    float a = (-45 + i) * Mathf.Deg2Rad;
                    outline.Add(new Vector3(Mathf.Sin(a) * range, 0.012f, Mathf.Cos(a) * range));
                }
                outline.Add(new Vector3(0f, 0.012f, 0f));
                Line(scene, outline.ToArray(), lineMaterial, new Color(1f, 0.67f, 0.22f, 0.7f), 0.025f);

                if (meterGrid == true)
                {
                    var gridColor = new Color(0.6f, 0.65f, 0.75f, 0.15f);
                    for (int i = -7; i <= 7; i++)
                    {
                        Line(scene, new[] { new Vector3(i, 0.005f, -1f), new Vector3(i, 0.005f, range + 2.5f) }, lineMaterial, gridColor, 0.008f);
                        Line(scene, new[] { new Vector3(-7f, 0.005f, i), new Vector3(7f, 0.005f, i) }, lineMaterial, gridColor, 0.008f);
                    }
                }

                var fx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
                ParticleSystem ps = fx.GetComponent<ParticleSystem>();
                rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                rt.Create();
                camera.targetTexture = rt;

                foreach (float time in times)
                {
                    ps.Simulate(time, true, true, false);
                    ps.Pause(true);
                    camera.Render();
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var png = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    png.Apply();
                    RenderTexture.active = previous;
                    string path = outputPath(time);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png);
                }

                if (mustBeDeadAt >= 0f)
                {
                    ps.Simulate(mustBeDeadAt, true, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (ps.IsAlive(true))
                        throw new Exception($"{Path.GetFileNameWithoutExtension(prefabPath)} still has live particles at {mustBeDeadAt}s");
                }
            }
            finally
            {
                RenderSettings.fog = fog;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (floorMaterial != null)
                    UnityEngine.Object.DestroyImmediate(floorMaterial);
                if (lineMaterial != null)
                    UnityEngine.Object.DestroyImmediate(lineMaterial);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Line(Scene scene, Vector3[] points, Material material, Color color, float width)
        {
            var go = new GameObject("PreviewLine");
            SceneManager.MoveGameObjectToScene(go, scene);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
        }
    }
}
