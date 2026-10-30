namespace RiftRaiders.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Loot beam on a dropped (recoverable) Signature Accessory - see docs/accessory-guard.md, "Missing-accessory
    /// feedback". A looping, attached effect (not an EffectsManager one-shot): it is nested in the AccessoryOrb
    /// prototype and DroppedAccessoryView switches it on once the pop arc lands, then tints every system to the
    /// owner's CharacterData.RingColor - so the layers are authored WHITE and only carry alpha/shape here.
    /// Layers: soft ground glow + expanding ground ring pulse + open light column + rising sparkles.
    /// 4 drawn systems, 2 materials (additive, alpha), ~20 particles alive.
    /// Edit the builder, not the assets.
    /// </summary>
    public static class AccessoryBeamFxBuilder
    {
        const string Folder = "Assets/_Project/Prefabs/Effects/Accessory";
        public const string PrefabPath = Folder + "/AccessoryBeamFx.prefab";
        const string OrbPrefabPath = "Assets/_QuantumUser/Entities/Prefabs/AccessoryOrb.prefab";

        // Footprint radius of the ring pulse, in world units (the pickup radius is 1).
        const float GroundRadius = .75f;
        const float ColumnRadius = .28f;
        const float ColumnHeight = 2.6f;

        [MenuItem("Tools/RiftRaiders/VFX/Accessory Beam/Build")]
        public static void Build()
        {
            if (AssetDatabase.IsValidFolder(Folder) == false)
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Effects", "Accessory");

            string prefix = Folder + "/AccessoryBeam";
            Material additive = MeshParticleFxKit.EnsureMaterial(prefix + "_Additive.mat", MeshParticleFxKit.AdditiveShader);
            Material alpha = MeshParticleFxKit.EnsureMaterial(prefix + "_Alpha.mat", MeshParticleFxKit.AlphaBlendedShader);
            Mesh disc = MeshParticleFxKit.SaveMesh(prefix + "_Disc.asset", MeshParticleFxKit.Disc("AccessoryBeamDisc", .7f, .35f));
            Mesh ring = MeshParticleFxKit.SaveMesh(prefix + "_Ring.asset", MeshParticleFxKit.Ring("AccessoryBeamRing", .8f, new[] { 0f, .7f, 1f, .5f, 0f }));
            // Open cylinder (Crown with bottom radius 1 = straight wall), bright at the floor fading out up top.
            Mesh column = MeshParticleFxKit.SaveMesh(prefix + "_Column.asset",
                MeshParticleFxKit.Crown("AccessoryBeamColumn", 1f, 1f, new[] { .9f, .75f, .45f, .2f, .06f, 0f }, 20));

            var root = new GameObject("AccessoryBeamFx");
            try
            {
                // Ground glow: two overlapping 1.2s discs cross-fading, so the glow breathes but never blinks out.
                var glow = Child(root, "GroundGlow", .03f);
                Looping(glow, 1.2f, 2f / 1.2f, 2);
                Size(glow, GroundRadius * .9f, new Keyframe(0f, .85f), new Keyframe(.5f, 1f), new Keyframe(1f, .85f));
                Fade(glow, 0f, .55f, .55f, 0f);
                MeshParticleFxKit.ApplyMeshRenderer(glow, disc, additive);
                glow.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;

                // Ring pulse: one ring every 0.8s swelling out to the footprint and fading - the "come here" beat.
                var pulse = Child(root, "RingPulse", .04f);
                Looping(pulse, .8f, 1f / .8f, 2);
                Size(pulse, GroundRadius, new Keyframe(0f, .25f, 3f, 3f), new Keyframe(.6f, .95f, .4f, .4f), new Keyframe(1f, 1.05f));
                Fade(pulse, 1f, 1f, .5f, 0f);
                MeshParticleFxKit.ApplyMeshRenderer(pulse, ring, alpha);
                pulse.GetComponent<ParticleSystemRenderer>().sortingFudge = 15f;

                // Light column: cross-fading open cylinders, stretched tall - reads from across the screen at the
                // 45 deg gameplay camera, where a flat ground mark is easy to lose under the crowd.
                var beam = Child(root, "Column", .02f);
                Looping(beam, 1.4f, 2f / 1.4f, 2);
                var beamMain = beam.main;
                beamMain.startSize3D = true;
                beamMain.startSizeX = ColumnRadius;
                beamMain.startSizeY = ColumnHeight;
                beamMain.startSizeZ = ColumnRadius;
                Fade(beam, 0f, .7f, .7f, 0f);
                MeshParticleFxKit.ApplyMeshRenderer(beam, column, additive);

                // Sparkles: small glints drifting up the column.
                var sparks = Child(root, "Sparkles", .1f);
                Looping(sparks, .9f, 9f, 12);
                var sparkMain = sparks.main;
                sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(.6f, .9f);
                sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                sparkMain.startSize = new ParticleSystem.MinMaxCurve(.1f, .2f);
                var sparkShape = sparks.shape;
                sparkShape.enabled = true;
                sparkShape.shapeType = ParticleSystemShapeType.Circle;
                sparkShape.radius = ColumnRadius;
                sparkShape.radiusThickness = .3f;
                sparkShape.rotation = new Vector3(90f, 0f, 0f);
                sparkShape.randomDirectionAmount = 0f;
                var upward = sparks.velocityOverLifetime;
                upward.enabled = true;
                upward.space = ParticleSystemSimulationSpace.Local;
                upward.x = new ParticleSystem.MinMaxCurve(0f);
                upward.y = new ParticleSystem.MinMaxCurve(.4f);
                upward.z = new ParticleSystem.MinMaxCurve(0f);
                Size(sparks, 1f, new Keyframe(0f, .4f), new Keyframe(.25f, 1f), new Keyframe(1f, 0f));
                Fade(sparks, 0f, 1f, .8f, 0f);
                var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
                sparkRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                sparkRenderer.sharedMaterial = additive;
                sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sparkRenderer.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[VFX] Accessory Beam built: " + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AttachToOrb();
        }

        // Nests the beam under the AccessoryOrb prototype (once) and assigns it to DroppedAccessoryView.landedBeam.
        static void AttachToOrb()
        {
            GameObject orb = PrefabUtility.LoadPrefabContents(OrbPrefabPath);
            try
            {
                var view = orb.GetComponent<Quantum.DroppedAccessoryView>();
                Transform existing = orb.transform.Find("AccessoryBeamFx");
                GameObject beam = existing != null ? existing.gameObject : null;

                if (beam == null)
                {
                    beam = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), orb.transform);
                    beam.name = "AccessoryBeamFx";
                }

                beam.transform.localPosition = Vector3.zero;
                beam.transform.localRotation = Quaternion.identity;
                beam.transform.localScale = Vector3.one;
                beam.SetActive(false);

                var so = new SerializedObject(view);
                so.FindProperty("landedBeam").objectReferenceValue = beam;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(orb, OrbPrefabPath);
                Debug.Log("[VFX] Accessory Beam attached to " + OrbPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(orb);
            }
        }

        static ParticleSystem Child(GameObject root, string name, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            return go.AddComponent<ParticleSystem>();
        }

        // White start colour on purpose: DroppedAccessoryView multiplies the owner's RingColor in at runtime.
        static void Looping(ParticleSystem ps, float lifetime, float rate, int maxParticles)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.duration = 1f;
            main.startLifetime = lifetime;
            main.startDelay = 0f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.playOnAwake = true;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 733;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ps.shape;
            shape.enabled = false;
        }

        static void Size(ParticleSystem ps, float startSize, params Keyframe[] keys)
        {
            var main = ps.main;
            if (main.startSize3D == false && main.startSize.mode == ParticleSystemCurveMode.Constant)
                main.startSize = startSize;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        // White, alpha `birth` -> `rise` at 25% -> `hold` at 70% -> `death`.
        static void Fade(ParticleSystem ps, float birth, float rise, float hold, float death)
        {
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(birth, 0f), new GradientAlphaKey(rise, .25f), new GradientAlphaKey(hold, .7f), new GradientAlphaKey(death, 1f) });
            fade.color = gradient;
        }
    }
}
