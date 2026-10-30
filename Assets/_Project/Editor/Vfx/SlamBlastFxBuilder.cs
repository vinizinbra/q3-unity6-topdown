namespace RiftRaiders.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Radius-scaled energy explosions, built per hero palette in one of two styles:
    ///  - Slam: Brute Aftershock (blue) - Juggernaut's closing blast, replayed by the rank 3 Earthquake DelayedBlast.
    ///    Squat dome + Groundbreaker-style white toon crown wall + floor ring and echo ring + flat splinters + smoke swirl.
    ///  - Blast: Yoru Vortex (purple) - Vortex Collapse explosion and Compression's Implosion. Hot core fireball +
    ///    round shell + one fast ring + debris thrown up into the air + smoke swirl; no wall.
    /// Radius 1 = blast radius (EffectsManager plays BlastEffectPrefab at Vector3.one * e.Radius). Silent root clock,
    /// 6 drawn systems, 3 materials (additive, alpha, the shared Wallcoeur DriftSmoke), <= 40 particles, dead by 0.7s.
    /// Edit the builder, not the assets.
    /// </summary>
    public static class SlamBlastFxBuilder
    {
        const string EffectsFolder = "Assets/_Project/Prefabs/Effects";

        sealed class Palette
        {
            public string Folder;   // under Prefabs/Effects
            public string Name;     // asset prefix, prefab = <Name>Fx.prefab
            public Color Core;      // flash / splinter tips / ring birth (near white)
            public Color Main;      // the hue the blast reads as
            public Color Deep;      // dark end the dome and ring fade into
            public Color WallTint;  // what the white crown wall fades to
            public bool Blast;      // Blast style (explosion) instead of Slam style (ground slam)

            public string Prefab => $"{EffectsFolder}/{Folder}/{Name}Fx.prefab";
        }

        static readonly Palette BruteAftershock = new Palette
        {
            Folder = "Brute", Name = "BruteAftershock",
            Core = new Color(.85f, .97f, 1f), Main = new Color(0f, .85f, 1f), Deep = new Color(0f, .35f, 1f),
            WallTint = new Color(.5f, .85f, 1f),
        };

        static readonly Palette YoruVortex = new Palette
        {
            Folder = "Yoru", Name = "YoruVortexBlast",
            Core = new Color(.96f, .88f, 1f), Main = new Color(.68f, .3f, 1f), Deep = new Color(.38f, .05f, .9f),
            WallTint = new Color(.78f, .55f, 1f), Blast = true,
        };

        public static string BruteAftershockPrefab => BruteAftershock.Prefab;
        public static string YoruVortexPrefab => YoruVortex.Prefab;

        [MenuItem("Tools/RiftRaiders/VFX/Slam Blast/Build Brute Aftershock")]
        public static void BuildBruteAftershock() => Build(BruteAftershock);

        [MenuItem("Tools/RiftRaiders/VFX/Slam Blast/Build Yoru Vortex")]
        public static void BuildYoruVortex() => Build(YoruVortex);

        // Grass floor, radius 1 plus the real gameplay radius (Aftershock base 4, Vortex collider ~3).
        [MenuItem("Tools/RiftRaiders/VFX/Slam Blast/Render Previews")]
        public static void RenderPreviews()
        {
            Preview(BruteAftershock, 4f);
            Preview(YoruVortex, 3f);
            Debug.Log("[VFX] Slam Blast previews rendered to docs/previews/slam-blast");
        }

        static void Preview(Palette palette, float gameplayRadius)
        {
            var grass = new Color(.42f, .56f, .33f);
            float[] times = { .03f, .08f, .2f, .4f };
            foreach (float radius in new[] { 1f, gameplayRadius })
                MeshParticleFxKit.RenderCirclePreview(palette.Prefab, radius, grass, times,
                    (_, time) => $"docs/previews/slam-blast/{palette.Name}-r{radius:0}-{Mathf.RoundToInt(time * 1000):000}ms.png",
                    meterGrid: true, mustBeDeadAt: .8f);
        }

        static void Build(Palette palette)
        {
            string folder = $"{EffectsFolder}/{palette.Folder}";
            if (AssetDatabase.IsValidFolder(folder) == false)
                AssetDatabase.CreateFolder(EffectsFolder, palette.Folder);
            string prefix = $"{folder}/{palette.Name}";
            Mesh SaveMesh(string suffix, Mesh mesh) => MeshParticleFxKit.SaveMesh(prefix + suffix + ".asset", mesh);

            Material additive = MeshParticleFxKit.EnsureMaterial(prefix + "_Additive.mat", MeshParticleFxKit.AdditiveShader);
            Material alpha = MeshParticleFxKit.EnsureMaterial(prefix + "_Alpha.mat", MeshParticleFxKit.AlphaBlendedShader);
            Mesh flashDisc = SaveMesh("_FlashDisc", MeshParticleFxKit.Disc("SlamBlastFlashDisc", .9f, .55f));
            Mesh dome = SaveMesh("_Dome", palette.Blast
                ? MeshParticleFxKit.Dome("SlamBlastDome", .85f, 1f, .3f)
                : MeshParticleFxKit.Dome("SlamBlastDome", .5f, 1f, .18f));
            Mesh ring = SaveMesh("_Ring", MeshParticleFxKit.Ring("SlamBlastRing", .86f, new[] { 0f, .55f, 1f, 1f, .4f, 0f }));
            Mesh shard = SaveMesh("_Shard", MeshParticleFxKit.Shard("SlamBlastShard", .45f));

            var root = new GameObject(palette.Name + "Fx");
            try
            {
                // Silent clock: EffectsManager needs a root ParticleSystem, and the root sits exactly on the floor (a
                // visible floor layer there z-fights), so every layer is a lifted child.
                var clock = root.AddComponent<ParticleSystem>();
                Configure(clock, .01f, Color.clear, 0f, 1);
                var clockEmission = clock.emission;
                clockEmission.enabled = false;
                clock.GetComponent<ParticleSystemRenderer>().enabled = false;

                // Flash 0-0.1s: the hit frame, a near-white disc snapping out to ~90% of the blast.
                var flash = Child(root, "Flash", .03f);
                Configure(flash, .1f, palette.Core, 0f, 1);
                SizeCurve(flash, new Keyframe(0f, .35f, 6f, 6f), new Keyframe(.5f, .9f, .4f, .4f), new Keyframe(1f, .95f));
                Fade(flash, Color.white, palette.Main, 1f, .6f, .25f);
                MeshParticleFxKit.ApplyMeshRenderer(flash, flashDisc, additive);

                // Energy body 0-0.34s: saturated squat shell (alpha blended, so the hue survives any floor) punching to
                // the radius, pale to the deep hue.
                var body = Child(root, "EnergyDome", .01f);
                Configure(body, .34f, Color.white, 0f, 1);
                SizeCurve(body, new Keyframe(0f, .2f, 5f, 5f), new Keyframe(.4f, .92f, .6f, .6f), new Keyframe(1f, 1.02f));
                Fade(body, palette.Core, palette.Deep, .95f, .75f, .12f, palette.Main);
                MeshParticleFxKit.ApplyMeshRenderer(body, dome, alpha);
                body.GetComponent<ParticleSystemRenderer>().sortingFudge = 10f;

                if (palette.Blast == true)
                    BuildBlastLayers(root, palette, SaveMesh, additive, alpha, ring, shard);
                else
                    BuildSlamLayers(root, palette, SaveMesh, additive, alpha, ring, shard);

                PrefabUtility.SaveAsPrefabAsset(root, palette.Prefab);
                AssetDatabase.SaveAssets();
                Validate(AssetDatabase.LoadAssetAtPath<GameObject>(palette.Prefab));
                Debug.Log("[VFX] Slam Blast built: " + palette.Prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void BuildSlamLayers(GameObject root, Palette palette, Func<string, Mesh, Mesh> saveMesh, Material additive, Material alpha, Mesh ring, Mesh shard)
        {
            Mesh crown = saveMesh("_Crown", MeshParticleFxKit.Crown("SlamBlastCrown", .42f, .8f, new[] { .2f, 1f, 1f, .9f, .45f, 0f }));

            // Toon slam wall 0.02-0.4s: white crown rises fast, rides out past the edge (overshoot) and collapses.
            var wave = Child(root, "ToonCrown", .02f);
            Configure(wave, .38f, Color.white, .02f, 1);
            var waveSize = wave.sizeOverLifetime;
            waveSize.enabled = true;
            waveSize.separateAxes = true;
            var spread = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .15f, 4.5f, 4.5f), new Keyframe(.4f, .9f, .9f, .9f), new Keyframe(.7f, 1.05f, 0f, 0f), new Keyframe(1f, 1f)));
            waveSize.x = spread;
            waveSize.z = spread;
            waveSize.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .25f, 6f, 6f), new Keyframe(.25f, 1f, 0f, 0f), new Keyframe(.7f, .55f), new Keyframe(1f, .05f)));
            Fade(wave, Color.white, palette.WallTint, 1f, 1f, .55f);
            MeshParticleFxKit.ApplyMeshRenderer(wave, crown, alpha);

            // Floor ring + echo 0.02-0.66s: the front lands exactly on the radius, a second pulse 0.1s later.
            var front = Child(root, "ShockRing", .035f);
            Configure(front, .55f, palette.Main, .02f, 2);
            var frontMain = front.main;
            frontMain.duration = .12f;
            var frontBursts = front.emission;
            frontBursts.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(.1f, 1) });
            SizeCurve(front, new Keyframe(0f, .18f, 4f, 4f), new Keyframe(.45f, 1.05f, .2f, .2f), new Keyframe(1f, 1f));
            Fade(front, palette.Core, palette.Deep, 1f, .9f, .4f, palette.Main);
            MeshParticleFxKit.ApplyMeshRenderer(front, ring, alpha);
            front.GetComponent<ParticleSystemRenderer>().sortingFudge = 15f;

            // Splinters 0-0.36s: shards thrown outward along their travel, popping up a little.
            var splinters = Child(root, "Splinters", .15f);
            Configure(splinters, .36f, Color.white, 0f, 16);
            var splinterMain = splinters.main;
            splinterMain.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .36f);
            splinterMain.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
            splinterMain.startSize = new ParticleSystem.MinMaxCurve(.16f, .3f);
            splinterMain.startColor = new ParticleSystem.MinMaxGradient(palette.Core, palette.Main);
            splinterMain.gravityModifier = .6f;
            var splinterShape = splinters.shape;
            splinterShape.enabled = true;
            splinterShape.shapeType = ParticleSystemShapeType.Circle;
            splinterShape.radius = .12f;
            splinterShape.rotation = new Vector3(90f, 0f, 0f);
            splinterShape.alignToDirection = true;
            splinterShape.randomDirectionAmount = .08f;
            var lift = splinters.velocityOverLifetime;
            lift.enabled = true;
            lift.space = ParticleSystemSimulationSpace.Local;
            lift.x = new ParticleSystem.MinMaxCurve(0f);
            lift.y = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
            lift.z = new ParticleSystem.MinMaxCurve(0f);
            SizeCurve(splinters, new Keyframe(0f, 1f), new Keyframe(.6f, .8f), new Keyframe(1f, 0f));
            Fade(splinters, Color.white, Color.white, 1f, 1f, .7f);
            MeshParticleFxKit.ApplyMeshRenderer(splinters, shard, additive);

            SmokeSwirl(root, palette);
        }

        static void BuildBlastLayers(GameObject root, Palette palette, Func<string, Mesh, Mesh> saveMesh, Material additive, Material alpha, Mesh ring, Mesh shard)
        {
            Mesh ball = saveMesh("_CoreBall", MeshParticleFxKit.Dome("SlamBlastCoreBall", 1f, .35f, 1f, 20, 4));

            // Hot core 0-0.22s: a white-hot fireball swelling at the centre, the "explosion" read the slam doesn't have.
            var core = Child(root, "HotCore", .02f);
            Configure(core, .22f, Color.white, 0f, 1);
            SizeCurve(core, new Keyframe(0f, .12f, 6f, 6f), new Keyframe(.35f, .5f, .5f, .5f), new Keyframe(1f, .62f));
            Fade(core, Color.white, palette.Main, 1f, 1f, .45f);
            MeshParticleFxKit.ApplyMeshRenderer(core, ball, additive);

            // Shock ring 0.02-0.42s: one fast front landing on the radius.
            var front = Child(root, "ShockRing", .035f);
            Configure(front, .4f, palette.Main, .02f, 1);
            SizeCurve(front, new Keyframe(0f, .2f, 5f, 5f), new Keyframe(.35f, 1.05f, .2f, .2f), new Keyframe(1f, 1f));
            Fade(front, palette.Core, palette.Deep, 1f, .9f, .35f, palette.Main);
            MeshParticleFxKit.ApplyMeshRenderer(front, ring, alpha);
            front.GetComponent<ParticleSystemRenderer>().sortingFudge = 15f;

            // Debris 0-0.5s: shards thrown up and out of the blast in 3D, arcing back down.
            var debris = Child(root, "Debris", .2f);
            Configure(debris, .5f, Color.white, 0f, 20);
            var debrisMain = debris.main;
            debrisMain.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .5f);
            debrisMain.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 4.4f);
            debrisMain.startSize = new ParticleSystem.MinMaxCurve(.14f, .28f);
            debrisMain.startColor = new ParticleSystem.MinMaxGradient(palette.Core, palette.Main);
            debrisMain.gravityModifier = 1.4f;
            var debrisShape = debris.shape;
            debrisShape.enabled = true;
            debrisShape.shapeType = ParticleSystemShapeType.Hemisphere;
            debrisShape.radius = .1f;
            debrisShape.rotation = new Vector3(-90f, 0f, 0f);
            debrisShape.alignToDirection = true;
            var debrisLift = debris.velocityOverLifetime;
            debrisLift.enabled = true;
            debrisLift.space = ParticleSystemSimulationSpace.Local;
            debrisLift.x = new ParticleSystem.MinMaxCurve(0f);
            debrisLift.y = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            debrisLift.z = new ParticleSystem.MinMaxCurve(0f);
            SizeCurve(debris, new Keyframe(0f, 1f), new Keyframe(.7f, .75f), new Keyframe(1f, 0f));
            Fade(debris, Color.white, Color.white, 1f, 1f, .8f);
            MeshParticleFxKit.ApplyMeshRenderer(debris, shard, additive);

            SmokeSwirl(root, palette);
        }

        // Smoke swirl 0.03-0.63s (both styles): Groundbreaker ToonSlam's rotating smoke - Wallcoeur's animated DriftSmoke
        // sheet on its radius-1 half-sphere, swelling to the blast radius while it turns, tinted to the palette.
        static void SmokeSwirl(GameObject root, Palette palette)
        {
            const string WallcoeurImpact = "Assets/3rd-party/CartoonVFX by Wallcoeur/VFX_Bundle_Wallcoeur/03_MATERIALS/";
            var swirl = Child(root, "SmokeSwirl", .2f); // the half-sphere dips 0.2 below its pivot
            Configure(swirl, .6f, Color.Lerp(palette.Core, palette.Main, .35f), .03f, 1);
            var swirlMain = swirl.main;
            swirlMain.startRotation3D = true;
            swirlMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var size = swirl.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            var spread = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .2f, 6f, 6f), new Keyframe(.15f, .75f, 1.2f, 1.2f), new Keyframe(1f, 1f)));
            size.x = spread;
            size.z = spread;
            size.y = new ParticleSystem.MinMaxCurve(.6f, new AnimationCurve(new Keyframe(0f, .2f, 6f, 6f), new Keyframe(.15f, .75f, 1.2f, 1.2f), new Keyframe(1f, 1f)));
            var spin = swirl.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(0f);
            spin.y = new ParticleSystem.MinMaxCurve(Mathf.PI);
            spin.z = new ParticleSystem.MinMaxCurve(0f);
            var sheet = swirl.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.9999f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;
            Fade(swirl, Color.white, palette.Main, 1f, 1f, .6f);
            MeshParticleFxKit.ApplyMeshRenderer(swirl,
                AssetDatabase.LoadAssetAtPath<Mesh>(WallcoeurImpact + "03_Model/DemiSphere.fbx"),
                AssetDatabase.LoadAssetAtPath<Material>(WallcoeurImpact + "Impact/M_Fp_DriftSmoke05ADD.mat"));
        }

        static ParticleSystem Child(GameObject root, string name, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            return go.AddComponent<ParticleSystem>();
        }

        static void Configure(ParticleSystem ps, float lifetime, Color color, float delay, int count)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = .05f;
            main.startLifetime = lifetime;
            main.startDelay = delay;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = count;
            main.playOnAwake = true;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 517;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.enabled = false;
        }

        static void SizeCurve(ParticleSystem ps, params Keyframe[] keys)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        // Colour from `start` to `end` (through `mid` at 35% when given); alpha `birth`, `hold` at 35%, `late` at 70%, 0 at death.
        static void Fade(ParticleSystem ps, Color start, Color end, float birth, float hold, float late, Color? mid = null)
        {
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            GradientColorKey[] colors = mid.HasValue
                ? new[] { new GradientColorKey(start, 0f), new GradientColorKey(mid.Value, .35f), new GradientColorKey(end, 1f) }
                : new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) };
            gradient.SetKeys(colors, new[] { new GradientAlphaKey(birth, 0f), new GradientAlphaKey(hold, .35f), new GradientAlphaKey(late, .7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
        }

        static void Validate(GameObject prefab)
        {
            if (prefab.GetComponent<ParticleSystem>() == null)
                throw new Exception("Missing root ParticleSystem");
            foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.loop == true || main.startDelay.constantMax + main.startLifetime.constantMax > .8f)
                    throw new Exception("Invalid one-shot timing: " + ps.name);
                if (main.scalingMode != ParticleSystemScalingMode.Hierarchy)
                    throw new Exception("Not Hierarchy scaling (breaks the radius-1 contract): " + ps.name);
            }
        }
    }
}
