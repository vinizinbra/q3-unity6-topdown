namespace RiftRaiders.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Dono do Céu's whistle hit ("A Geral Não Perdoa"): a piercing shriek riding the exact 90 deg x range cone - same
    /// family as Riff Riot's Power Chord (feathered mesh arcs, MeshParticleFxKit) but its own voice: two thick wavefronts,
    /// straight shriek rays instead of forked lightning, all white (the whistle attack's colour) held by a thin dark ink
    /// line so it reads on the Favela's light concrete (alpha-blended, not additive neon, which washes out on a bright
    /// floor). Local +Z = forward, apex at the origin; one-shot, gone by 0.8s. Called by DonoDoCeuBossBuilder.
    /// </summary>
    public static class DonoDoCeuWhistleFxBuilder
    {
        private const string Prefix = "Assets/_Project/Prefabs/Effects/DonoDoCeu/DonoDoCeu_WhistleShriek";
        public const string PrefabPath = Prefix + ".prefab";

        private static readonly Color Ink = new Color(0.1f, 0.1f, 0.14f, 0.85f);    // cartoon outline under the white
        private static readonly Color White = new Color(0.97f, 0.98f, 1f, 1f);
        private static readonly float[] ArcProfile = { 0f, 0.35f, 0.8f, 1f, 0.8f, 0.35f, 0f };

        public static ParticleSystem Build(float range)
        {
            Material additive = MeshParticleFxKit.EnsureMaterial(Prefix + "_Glow.mat", MeshParticleFxKit.AdditiveShader);
            Material alpha = MeshParticleFxKit.EnsureMaterial(Prefix + "_Body.mat", MeshParticleFxKit.AlphaBlendedShader);
            Mesh outline = SaveMesh("_Outline", MeshParticleFxKit.Arc("WhistleArc", 0.21f, 1f, ArcProfile, 6f));
            Mesh body = SaveMesh("_Arc", MeshParticleFxKit.Arc("WhistleArc", 0.16f, 0.985f, ArcProfile, 6f));
            Mesh core = SaveMesh("_Core", MeshParticleFxKit.Arc("WhistleArc", 0.05f, 0.95f, ArcProfile, 6f));
            Mesh halo = SaveMesh("_Halo", MeshParticleFxKit.Arc("WhistleArc", 0.34f, 1f, ArcProfile, 6f));
            Mesh flash = SaveMesh("_ConeFlash", MeshParticleFxKit.SoftCone("WhistleConeFlash", 0.5f, 0.8f));
            const float mouth = 0.9f;

            var root = new GameObject("DonoDoCeu_WhistleShriek");
            try
            {
                ParticleSystem clock = root.AddComponent<ParticleSystem>();
                Configure(clock, 0.7f, 0.01f, Color.white, 0f);
                ParticleSystem.EmissionModule clockEmission = clock.emission;
                clockEmission.enabled = false;
                clock.GetComponent<ParticleSystemRenderer>().enabled = false;

                // Two thick wavefronts 90ms apart, fast out of the beak, easing onto the cone's edge.
                for (int i = 0; i < 2; i++)
                {
                    float delay = i * 0.09f;
                    float end = range - i * 0.4f;
                    Wave(root, $"Wave{i + 1}_Halo", halo, additive, new Color(1f, 1f, 1f, 0.35f), delay, 0.36f, mouth, end, 0.10f, easeOut: true);
                    Wave(root, $"Wave{i + 1}_Outline", outline, alpha, Ink, delay, 0.36f, mouth, end, 0.12f, easeOut: true);
                    Wave(root, $"Wave{i + 1}_Arc", body, alpha, White, delay, 0.36f, mouth, end, 0.14f, easeOut: true);
                    Wave(root, $"Wave{i + 1}_Core", core, alpha, Color.white, delay, 0.36f, mouth, end, 0.16f, easeOut: true);
                }

                // Shriek rays: straight, thin, staggered flickers fanning across the cone - the pitch, not a discharge.
                for (int i = 0; i < 2; i++)
                {
                    Mesh rays = SaveMesh($"_Rays{i}", Rays(i, (mouth + 0.3f) / range));
                    Flicker(Wave(root, $"Rays{i}_Shadow", rays, alpha, new Color(Ink.r, Ink.g, Ink.b, 0.45f), i * 0.06f, 0.11f, range, range, 0.11f));
                    Flicker(Wave(root, $"Rays{i}_Core", rays, additive, new Color(1f, 1f, 1f, 0.9f), i * 0.06f, 0.11f, range, range, 0.13f));
                }

                Wave(root, "ConeFlash", flash, alpha, new Color(1f, 1f, 1f, 0.32f), 0f, 0.1f, range, range, 0.09f);
                Wave(root, "BeakSnap_Outline", outline, alpha, Ink, 0f, 0.14f, 0.5f, 2.4f, 0.18f);
                Wave(root, "BeakSnap_Core", core, alpha, Color.white, 0f, 0.12f, 0.45f, 2.2f, 0.2f);
                Wave(root, "FarEdge_Ring", body, alpha, new Color(1f, 1f, 1f, 0.5f), 0.3f, 0.22f, range - 0.15f, range, 0.12f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(PrefabPath);
        }

        private static Mesh SaveMesh(string suffix, Mesh mesh) => MeshParticleFxKit.SaveMesh(Prefix + suffix + ".asset", mesh);

        // One-shot, single burst of one particle, faded out over its life.
        private static void Configure(ParticleSystem ps, float duration, float lifetime, Color color, float delay)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = lifetime;
            main.startDelay = delay;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 1;
            main.playOnAwake = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 517;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            SetFade(ps, new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.4f), new GradientAlphaKey(0.45f, 0.75f), new GradientAlphaKey(0f, 1f));
        }

        // One mesh particle scaling from `from` to `to` (mesh radius 1 = `to` metres at the end); easeOut = fast off the
        // beak, settling onto the edge, else linear.
        private static ParticleSystem Wave(GameObject root, string name, Mesh mesh, Material material, Color color, float delay, float life,
            float from, float to, float height, bool easeOut = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            Configure(ps, 0.01f, life, color, delay);
            ParticleSystem.MainModule main = ps.main;
            main.startSize = to;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = easeOut == true
                ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from / to, 3f, 3f), new Keyframe(0.4f, 0.8f, 0.8f, 0.8f), new Keyframe(1f, 1f, 0.1f, 0.1f)))
                : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from / to, 1f, 1f));

            MeshParticleFxKit.ApplyMeshRenderer(ps, mesh, material);
            return ps;
        }

        // Rays flick on, hold a beat and die out instead of the default fade.
        private static void Flicker(ParticleSystem ps) =>
            SetFade(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.55f, 0.35f), new GradientAlphaKey(0f, 1f));

        private static void SetFade(ParticleSystem ps, params GradientAlphaKey[] alphaKeys)
        {
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphaKeys);
            fade.color = gradient;
        }

        // Straight rays from just past the beak (`start`, in cone radii) toward the edge, tapering at both ends;
        // `variant` interleaves two sets of angles.
        private static Mesh Rays(int variant, float start)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            var random = new System.Random(311 + variant * 97);
            const float width = 0.02f;

            for (int ray = 0; ray < 6; ray++)
            {
                float angle = (-36f + ray * 14.4f + variant * 7.2f + (float)(random.NextDouble() - 0.5) * 3f) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var normal = new Vector3(direction.z, 0f, -direction.x);
                float end = 0.7f + (float)random.NextDouble() * 0.22f;
                int first = vertices.Count;

                for (int k = 0; k <= 8; k++)
                {
                    float t = k / 8f;
                    float taper = Mathf.Sin(t * Mathf.PI);
                    for (int row = 0; row < 3; row++)
                    {
                        vertices.Add(direction * Mathf.Lerp(start, end, t) + normal * (row - 1) * width * (0.4f + taper));
                        colors.Add(new Color(1f, 1f, 1f, row == 1 ? taper : 0f));
                        if (k < 8 && row < 2)
                            MeshParticleFxKit.Quad(indices, first + k * 3 + row, 3);
                    }
                }
            }

            return MeshParticleFxKit.BuildMesh("WhistleRays", vertices, colors, indices);
        }

        // Gameplay-camera stills on a Favela-concrete grey floor, written to Recordings/.
        [MenuItem("Tools/RiftRaiders/Boss/Dono do Céu/Render whistle FX preview")]
        public static void RenderPreview()
        {
            float range = DonoDoCeuBossBuilder.WhistleRange;
            MeshParticleFxKit.RenderConePreview(PrefabPath, range, new Color(0.62f, 0.62f, 0.6f), range * 0.45f,
                new[] { 0.03f, 0.08f, 0.15f, 0.25f, 0.4f }, time => $"Recordings/DonoDoCeu_WhistleShriek_{Mathf.RoundToInt(time * 1000):000}ms.png");
        }
    }
}
