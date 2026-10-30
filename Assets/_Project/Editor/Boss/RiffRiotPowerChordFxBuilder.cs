namespace RiftRaiders.Editor
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;
    using UnityEngine.SceneManagement;

    /// <summary>Independent, opt-in authoring and deterministic Editor preview of the hand-authored hit effect.</summary>
    public static class RiffRiotPowerChordFxBuilder
    {
        const string Folder = "Assets/_Project/Prefabs/Effects/RiffRiot";
        const string Prefix = Folder + "/RiffRiot_PowerChord";
        const string Prefab = Prefix + "Fx.prefab";
        static readonly Color Pink = new Color(253f / 255f, 57f / 255f, 113f / 255f);

        [MenuItem("Tools/RiftRaiders/Boss/Power Chord/Build independent prefab")]
        public static void Build()
        {
            var additive = MeshParticleFxKit.EnsureMaterial(Prefix + "_Neon.mat", MeshParticleFxKit.AdditiveShader);
            var alpha = MeshParticleFxKit.EnsureMaterial(Prefix + "_Pink.mat", MeshParticleFxKit.AlphaBlendedShader);
            var aura = SaveMesh("_Aura", Arc(.28f, 1f));
            var halo = SaveMesh("_Halo", Arc(.18f, .95f));
            var body = SaveMesh("_Body", Arc(.07f, .895f));
            var tube = SaveMesh("_Tube", Arc(.070f, .895f));
            var core = SaveMesh("_Core", Arc(.032f, .876f));
            var sector = SaveMesh("_Sector", Sector());
            var shard = SaveMesh("_Shard", Shard());
            var flash = SaveMesh("_ConeFlash", MeshParticleFxKit.SoftCone("SoftFullConeImpactFlash", .6f, .78f));
            var root = new GameObject("RiffRiot_PowerChordFx");
            try
            {
                var clock = root.AddComponent<ParticleSystem>();
                Configure(clock, .7f, .01f, Color.white, 0, 1);
                var e = clock.emission; e.enabled = false;
                clock.GetComponent<ParticleSystemRenderer>().enabled = false;

                // Three nested ')' wavefronts. Vertex feathering stays INSIDE the exact angular/radial bounds.
                for (int i = 0; i < 3; i++)
                {
                    float delay = i * .055f;
                    float endRadius = 6.5f - i * .18f;
                    Wave(root, "Wave" + (i + 1) + "_Aura", aura, additive, new Color(1f, .045f, .40f, .90f), delay, .40f, .65f, endRadius, .10f);
                    Wave(root, "Wave" + (i + 1) + "_Halo", halo, additive, new Color(1f, .16f, .55f, 1f), delay, .40f, .65f, endRadius, .125f);
                    Wave(root, "Wave" + (i + 1) + "_Pink", body, alpha, Pink, delay, .40f, .65f, endRadius, .15f);
                    Wave(root, "Wave" + (i + 1) + "_Tube", tube, additive, new Color(1f, .45f, .75f, 1f), delay, .40f, .65f, endRadius, .175f);
                    Wave(root, "Wave" + (i + 1) + "_Core", core, additive, new Color(1f, .96f, .99f, 1f), delay, .40f, .65f, endRadius, .20f);
                }

                // Short, discontinuous discharges fill the struck cone at birth, then flicker out.
                for (int i = 0; i < 3; i++)
                {
                    var boltHalo = SaveMesh("_LightningHalo" + i, Lightning(.17f, i, false));
                    var boltCore = SaveMesh("_LightningCore" + i, Lightning(.027f, i, true));
                    float delay = i * .065f;
                    float life = i == 0 ? .095f : .08f;
                    Wave(root, "Lightning" + i + "_Glow", boltHalo, additive, new Color(1, 1, 1, .8f), delay, life, 1, 1, .12f);
                    Wave(root, "Lightning" + i + "_Core", boltCore, additive, new Color(1, 1, 1, .9f), delay, life, 1, 1, .125f);
                }
                Wave(root, "ConeFlash_Pink", flash, additive, new Color(1f, .06f, .35f, .8f), 0, .09f, 6.5f, 6.5f, .10f);
                Wave(root, "ConeFlash_White", flash, additive, new Color(1f, .82f, .94f, .9f), 0, .075f, 6.5f, 6.5f, .105f);
                Wave(root, "Impact_FullConeHalo", aura, additive, new Color(1f, .18f, .55f, .7f), 0, .12f, 6.48f, 6.48f, .10f);
                Wave(root, "Impact_WhiteCrack", tube, additive, new Color(1f, .85f, .96f, 1f), 0, .10f, 6.48f, 6.48f, .12f);

                // Instant response across the struck area, never a travelling damage warning.
                foreach (float r in new[] { 1.4f, 2.8f, 4.3f, 6.48f })
                    Wave(root, "AttackTransient_" + r, body, additive, new Color(1f, .12f, .48f, .32f), 0, .14f, r, r, .10f);
                Wave(root, "GuitarSnap_Halo", aura, additive, new Color(1f, .22f, .6f, 1f), 0, .16f, .6f, 2.5f, .18f);
                Wave(root, "GuitarSnap_Core", tube, additive, new Color(1f, .96f, 1f), 0, .12f, .5f, 2.2f, .20f);
                Wave(root, "FarEdge_Resonance", halo, additive, new Color(1f, .05f, .6f, .20f), .28f, .24f, 6.40f, 6.48f, .10f);
                Wave(root, "FarEdge_StringEcho", core, additive, new Color(.25f, .9f, 1f, .28f), .32f, .18f, 6.38f, 6.46f, .12f);

                var sparks = Child(root, "StringSplinters", .16f);
                Configure(sparks, .01f, .32f, Pink, 0, 28);
                var sm = sparks.main;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .36f);
                sm.startSize = new ParticleSystem.MinMaxCurve(.14f, .28f);
                sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .8f, .93f), new Color(.3f, .95f, 1f));
                sm.startRotation = new ParticleSystem.MinMaxCurve(-.7f, .7f);
                var shape = sparks.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Mesh;
                shape.mesh = sector; shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                shape.scale = new Vector3(5.9f, 1f, 5.9f);
                var emission = sparks.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 24) });
                MeshParticleFxKit.ApplyMeshRenderer(sparks, shard, additive);
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                AssetDatabase.SaveAssets();
                Validate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Configure(ParticleSystem ps, float duration, float lifetime, Color color, float delay, int count)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.duration = duration; main.startLifetime = lifetime;
            main.startDelay = delay; main.startSpeed = 0; main.startSize = 1;
            main.startColor = color; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = count;
            main.playOnAwake = true; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed = false; ps.randomSeed = 731;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
            var shape = ps.shape; shape.enabled = false;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.95f, .35f), new GradientAlphaKey(.5f, .7f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
        }

        static ParticleSystem Child(GameObject root, string name, float height)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0, height, 0);
            return go.AddComponent<ParticleSystem>();
        }

        static void Wave(GameObject root, string name, Mesh mesh, Material material, Color color, float delay, float life, float from, float to, float height)
        {
            var ps = Child(root, name, height);
            Configure(ps, .01f, life, color, delay, 1);
            var main = ps.main; main.startSize = to;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, from / to, 1, 1));
            if (name.StartsWith("Wave", StringComparison.Ordinal))
                size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                    new Keyframe(0, from / to, 2.3f, 2.3f), new Keyframe(.45f, .74f, .9f, .9f), new Keyframe(1, 1, .15f, .15f)));
            if (name.StartsWith("Lightning", StringComparison.Ordinal))
            {
                var fade = ps.colorOverLifetime;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .06f), new GradientAlphaKey(.65f, .3f),
                        new GradientAlphaKey(.12f, .7f), new GradientAlphaKey(0, 1) });
                fade.color = gradient;
            }
            if (name.StartsWith("ConeFlash", StringComparison.Ordinal))
            {
                if (name == "ConeFlash_White")
                {
                    var flashMain = ps.main; flashMain.maxParticles = 8;
                    var flashEmission = ps.emission;
                    flashEmission.SetBursts(new[] { new ParticleSystem.Burst(0, 8) });
                }
                var fade = ps.colorOverLifetime;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.95f, .12f), new GradientAlphaKey(.3f, .45f),
                        new GradientAlphaKey(.04f, .8f), new GradientAlphaKey(0, 1) });
                fade.color = gradient;
            }
            MeshParticleFxKit.ApplyMeshRenderer(ps, mesh, material);
        }

        static Mesh Lightning(float width, int variant, bool hotCore)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var indices = new List<int>();
            var random = new System.Random(925 + variant * 73);
            for (int strand = 0; strand < 7; strand++)
            {
                var points = new List<Vector3>();
                float baseAngle = -28 + strand * 9.3f;
                for (int k = 0; k < 9; k++)
                {
                    float radius = .85f + k * .66f;
                    float a = (baseAngle + (float)(random.NextDouble() - .5) * (k == 0 ? 2 : 9)) * Mathf.Deg2Rad;
                    points.Add(new Vector3(Mathf.Sin(a) * radius, .10f + (float)random.NextDouble() * .42f, Mathf.Cos(a) * radius));
                }
                Color tint = strand % 3 == 0 ? new Color(.15f, .8f, 1f) : new Color(1f, .08f, .48f);
                if (hotCore) tint = Color.Lerp(tint, Color.white, .85f);
                Ribbon(points, width, tint, vertices, colors, indices);
                // Two short forks per strand, with endpoints kept comfortably inside the damage sector.
                for (int fork = 0; fork < 2; fork++)
                {
                    var p = points[3 + fork * 2];
                    float a = (baseAngle + (strand % 2 == 0 ? 8 : -8)) * Mathf.Deg2Rad;
                    var end = new Vector3(Mathf.Sin(a) * (3.6f + fork), .55f, Mathf.Cos(a) * (3.6f + fork));
                    Ribbon(new List<Vector3> { p, Vector3.Lerp(p, end, .55f) + new Vector3(.08f, -.1f, 0), end },
                        width * .6f, tint, vertices, colors, indices);
                }
            }
            return Mesh("ForkedElectricChord", vertices, colors, indices);
        }

        static void Ribbon(List<Vector3> points, float width, Color tint, List<Vector3> v, List<Color> c, List<int> t)
        {
            // Corner cutting rounds the kinks without overshooting the original cone-contained path.
            for (int pass = 0; pass < 2; pass++)
            {
                var rounded = new List<Vector3> { points[0] };
                for (int i = 0; i < points.Count - 1; i++)
                {
                    rounded.Add(Vector3.Lerp(points[i], points[i + 1], .25f));
                    rounded.Add(Vector3.Lerp(points[i], points[i + 1], .75f));
                }
                rounded.Add(points[points.Count - 1]); points = rounded;
            }
            int first = v.Count;
            for (int i = 0; i < points.Count; i++)
            {
                var tangent = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(0, i - 1)];
                var normal = new Vector3(-tangent.z, 0, tangent.x).normalized;
                float cap = Mathf.SmoothStep(0, 1, Mathf.Min(i, points.Count - 1 - i) / 3f);
                for (int row = 0; row < 5; row++)
                {
                    v.Add(points[i] + normal * (row / 4f - .5f) * width * 2);
                    var color = tint; color.a = (row == 0 || row == 4 ? 0 : row == 2 ? 1 : .4f) * cap;
                    c.Add(color);
                    if (i < points.Count - 1 && row < 4) Quad(t, first + i * 5 + row, 5);
                }
            }
        }

        static Mesh SaveMesh(string suffix, Mesh mesh) => MeshParticleFxKit.SaveMesh(Prefix + suffix + ".asset", mesh);

        // Native mesh geometry with transparent feathered edges; no new raster textures or custom shaders.
        static Mesh Arc(float thickness, float outerRadius = 1f) =>
            MeshParticleFxKit.Arc("Feathered90DegreeArc", thickness, outerRadius, new[] { 0f, .23f, .65f, 1f, .65f, .23f, 0f }, 5f);
        static void Quad(List<int> t, int k, int stride) => MeshParticleFxKit.Quad(t, k, stride);
        static Mesh Mesh(string name, List<Vector3> v, List<Color> c, List<int> t) => MeshParticleFxKit.BuildMesh(name, v, c, t);

        static Mesh Sector()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= 32; i++)
            {
                float a = Mathf.Lerp(-35, 35, i / 32f) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(direction * .18f); v.Add(direction); c.Add(Color.white); c.Add(Color.white);
                if (i < 32) Quad(t, i * 2, 2);
            }
            return Mesh("InsetSparkSector", v, c, t);
        }
        static Mesh Shard()
        {
            return Mesh("SoftStringSplinter", new List<Vector3> { Vector3.zero, new Vector3(-.22f, 0, 0), new Vector3(0, 0, .65f), new Vector3(.22f, 0, 0), new Vector3(0, 0, -.65f) },
                new List<Color> { Color.white, Color.clear, Color.clear, Color.clear, Color.clear }, new List<int> { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 });
        }

        static void Validate(GameObject prefab)
        {
            if (!prefab.GetComponent<ParticleSystem>()) throw new Exception("Missing root ParticleSystem");
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main;
                if (m.loop || m.startDelay.constantMax + m.startLifetime.constantMax > .8f)
                    throw new Exception("Invalid one-shot timing: " + ps.name);
            }
        }

        [MenuItem("Tools/RiftRaiders/Boss/Power Chord/Validate cone and lifetime")]
        public static void ValidateCone()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Validate(prefab);
            var scene = EditorSceneManager.NewPreviewScene();
            float maxRadius = 0, maxAngle = 0, lastVisible = 0;
            int tested = 0;
            try
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                fx.transform.rotation = Quaternion.Euler(0, 73, 0); // Also exercise aim-relative mesh orientation.
                var root = fx.GetComponent<ParticleSystem>();
                for (int step = 0; step <= 80; step++)
                {
                    float time = step * .01f;
                    root.Simulate(time, true, true, false); root.Pause(true);
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                    {
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        if (!renderer.enabled || !renderer.mesh) continue;
                        var particles = new ParticleSystem.Particle[ps.main.maxParticles];
                        int count = ps.GetParticles(particles);
                        if (count > 0) lastVisible = time;
                        foreach (var vertex in renderer.mesh.vertices)
                        for (int i = 0; i < count; i++)
                        {
                            var p = particles[i];
                            var point = p.position + Quaternion.Euler(p.rotation3D) * Vector3.Scale(vertex, p.GetCurrentSize3D(ps));
                            point = fx.transform.InverseTransformPoint(ps.transform.TransformPoint(point));
                            float radius = new Vector2(point.x, point.z).magnitude;
                            float angle = radius > .0001f ? Mathf.Abs(Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg) : 0;
                            maxRadius = Mathf.Max(maxRadius, radius); maxAngle = Mathf.Max(maxAngle, angle); tested++;
                            if (radius > 6.501f || angle > 45.01f || point.z < -.001f)
                                throw new Exception($"Cone exceeded by {ps.name} at {time}: radius {radius}, angle {angle}");
                        }
                        if (time >= .8f && count > 0) throw new Exception("Particles survive 0.8s");
                    }
                }
                string report = $"PASS: root ParticleSystem; all systems non-looping; scale 1; local +Z.\n" +
                    $"81 samples, 0.00–0.80 seconds; yaw 73 degrees; {tested} mesh vertices checked.\n" +
                    $"Maximum radius: {maxRadius:F4} / 6.5 units. Maximum half-angle: {maxAngle:F4} / 45 degrees.\n" +
                    $"Last sampled live particle: {lastVisible:F2}s. No particles at 0.8s.\n" +
                    "Preview: Unity 6 URP, FOV 14, camera (0,36,-32.5), rotation (45,0,0), asphalt (0.40,0.41,0.47).\n" +
                    "No post processing, Bloom or fog. Orange outline and 1-unit grid are preview-only.\n";
                Directory.CreateDirectory("docs/previews/riff-riot-power-chord");
                File.WriteAllText("docs/previews/riff-riot-power-chord/validation.txt", report);
                Debug.Log(report);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/RiftRaiders/Boss/Power Chord/Render gameplay preview")]
        public static void RenderPreview()
        {
            MeshParticleFxKit.RenderConePreview(Prefab, 6.5f, new Color(.40f, .41f, .47f), 0f, new[] { 0f, .025f, .05f, .1f, .25f, .4f, .8f },
                time => "docs/previews/riff-riot-power-chord/power-chord-" + Mathf.RoundToInt(time * 1000).ToString("000") + "ms.png",
                meterGrid: true, mustBeDeadAt: .8f);
            Debug.Log("Power Chord: preview rendered without post processing; one-shot validation passed.");
        }
    }
}
