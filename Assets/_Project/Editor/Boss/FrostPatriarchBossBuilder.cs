namespace RiftRaiders.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using Quantum.Editor;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Builds the Arctic boss Frost Patriarch - "Patriarca da Geada" in game (docs/frost-patriarch-boss.md): sliced
    /// sprites, boss prefab, owned effect variants and every Quantum asset. Idempotent - re-running keeps every
    /// file (so every Unity and Quantum guid) and re-applies the tuning below, so THIS file is the single source of truth
    /// for the numbers; hand edits to the generated assets are overwritten. Templates (W1 boss prefab/asset, sticky puddle
    /// hazard, standard telegraph prefabs, shared knockback/damage effects, Epic Toon FX effects) are only read or copied,
    /// never modified. Nothing of W1/W2/Neon/Favela is touched.
    /// </summary>
    public static class FrostPatriarchBossBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Sprites/Enemy/Arctic/FrostPatriarch";
        private const string SheetAPath = ArtFolder + "/FrostPatriarch_SheetA.png";
        private const string SheetBPath = ArtFolder + "/FrostPatriarch_SheetB.png";
        private const string BodyMaterialPath = ArtFolder + "/FrostPatriarch_PinkCrystalGlow.mat";

        private const string Root = "Assets/_QuantumUser/Resources/Enemy/Arctic/Boss/FrostPatriarch";
        private const string BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/Arctic-FrostPatriarchPrefab.prefab";
        private const string RetiredIceTrailPrefabPath = "Assets/_QuantumUser/Entities/Enemies/Arctic-FrostPatriarchIceTrail.prefab";
        private const string VfxFolder = "Assets/_Project/Prefabs/Effects/FrostPatriarch";
        private const string RetiredProjectileFolder = "Assets/_QuantumUser/Entities/Projectile/FrostPatriarch"; // the jump slam's snowball ring (replaced by frost rings)

        // Templates (read/copied only).
        private const string W1BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/World1BossPrefab.prefab";
        private const string W1BossPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss.asset";
        private const string GenericDamagePath = "Assets/_QuantumUser/Resources/Enemy/_GenericActions/GenericDamageX1.asset";
        private const string KnockbackSmallPath = "Assets/_QuantumUser/Resources/HitEffects/KnockbackSmallEffectData.asset";
        private const string KnockbackMediumPath = "Assets/_QuantumUser/Resources/HitEffects/KnockbackMediumEffectData.asset";
        private const string KnockbackStrongPath = "Assets/_QuantumUser/Resources/HitEffects/KnockbackStrongEffectData.asset";
        private const string TelegraphFolder = "Assets/_QuantumUser/Resources/QuantumViewAssets/Telegraph";
        private const string GroundCrackPath = "Assets/_Project/Prefabs/Effects/GroundDecal.prefab"; // the W1 boss leap's landing crack
        private const string PunchFlashPath = "Assets/3rd-party/CartoonVFX by Wallcoeur/VFX_Bundle_Wallcoeur/01_PREFAB/3D_Simple_MuzzleFlash/MuzzleFlash_Pink (3).prefab"; // the W1 boss punch flash
        private const string SlashPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Sword/Slash/SwordSlashThin/SwordSlashThinRed.prefab";
        private const string ToonBodySlamPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Brawling/Soft/ToonBodySlam.prefab";
        private const string PillarBlastPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Magic/PillarBlast/MagicPillarBlastBlue.prefab";
        private const string FlamethrowerPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Flamethrower/Cartoon/FlamethrowerToonyBlue.prefab";
        private const string PyramidMeshPath = "Assets/_Project/Art/Primitives/PrototypePyramid.obj"; // the boss arena wall's spikes
        private const string W1SmokeChild = "VFX3D_Smoke";                                              // the elite pinkish-red smoke on the W1 boss rig
        private const string RiftWallMaterialPath = "Assets/_Project/Art/Material/RiftWall.mat";       // their material (copied, re-tinted to ice)
        private const string ShardTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/ice_spikes.png"; // 2x2 shard sheet
        private const string SnowTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/snow_2x2.png";
        private const string MistTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/cloud_2x2_soft.png";
        private const string JumpPuffGuid = "dbe2ea8610622df44a4a79ba99af5ddc"; // VFX3D_SimpleJump (W1 boss leap take-off)
        private const string W1BossMaterialName = "SpriteSelectiveColorGlow_PinkCrystal";

        // Production atlases derived from Assets/0_Refs/ArcticBoss/PatriarcaDaGeada/PatriarcaDaGeada-v1-SheetA.png and
        // PatriarcaDaGeada-v2-SheetB-Jump.png (v2: the centre pose is the leap jump) by
        // Source~/prep_frost_patriarch_sheets.py: every pose isolated from its connected components (the cells' stray
        // specks and the colour stored under zero alpha dropped, nothing redrawn), one pose per non-overlapping cell, all
        // at the source's one scale. Pivot: the silhouette's mass centre on the feet line - the torso stays put between
        // poses, the reared Icebreaker windup simply stands taller.
        private readonly struct SpriteSpec
        {
            public readonly string Name;
            public readonly string Sheet;
            public readonly Rect Rect;
            public readonly Vector2 Pivot;

            public SpriteSpec(string name, string sheet, Rect rect, Vector2 pivot)
            {
                Name = name;
                Sheet = sheet;
                Rect = rect;
                Pivot = pivot;
            }
        }

        private static readonly SpriteSpec[] Sprites =
        {
            new SpriteSpec("FrostPatriarch_Idle", "A", new Rect(83f, 880f, 473f, 302f), new Vector2(0.5019f, 0.0132f)),
            new SpriteSpec("FrostPatriarch_WalkA", "A", new Rect(726f, 880f, 467f, 276f), new Vector2(0.4811f, 0.0145f)),
            new SpriteSpec("FrostPatriarch_WalkB", "A", new Rect(1348f, 880f, 504f, 294f), new Vector2(0.4861f, 0.0136f)),
            new SpriteSpec("FrostPatriarch_SwipePrep", "A", new Rect(103f, 440f, 434f, 315f), new Vector2(0.4327f, 0.0127f)),
            new SpriteSpec("FrostPatriarch_SwipeRelease", "A", new Rect(701f, 440f, 518f, 271f), new Vector2(0.5117f, 0.0148f)),
            new SpriteSpec("FrostPatriarch_SwipeRecover", "A", new Rect(1356f, 440f, 488f, 300f), new Vector2(0.5032f, 0.0133f)),
            new SpriteSpec("FrostPatriarch_IcebreakerPrep", "A", new Rect(134f, 0f, 372f, 430f), new Vector2(0.5076f, 0.0093f)),
            new SpriteSpec("FrostPatriarch_IcebreakerImpact", "A", new Rect(717f, 0f, 485f, 328f), new Vector2(0.4763f, 0.0122f)),
            new SpriteSpec("FrostPatriarch_IcebreakerRecover", "A", new Rect(1364f, 0f, 472f, 324f), new Vector2(0.5067f, 0.0123f)),
            new SpriteSpec("FrostPatriarch_BreathPrep", "B", new Rect(74f, 880f, 491f, 330f), new Vector2(0.5125f, 0.0121f)),
            new SpriteSpec("FrostPatriarch_BreathActive", "B", new Rect(676f, 880f, 568f, 267f), new Vector2(0.5214f, 0.0150f)),
            new SpriteSpec("FrostPatriarch_BreathRecover", "B", new Rect(1373f, 880f, 453f, 296f), new Vector2(0.4746f, 0.0135f)),
            new SpriteSpec("FrostPatriarch_LeapPrep", "B", new Rect(77f, 440f, 485f, 280f), new Vector2(0.4728f, 0.0143f)),
            new SpriteSpec("FrostPatriarch_LeapJump", "B", new Rect(758f, 440f, 403f, 312f), new Vector2(0.4958f, 0.0128f)),
            new SpriteSpec("FrostPatriarch_LeapLand", "B", new Rect(1384f, 440f, 431f, 310f), new Vector2(0.4737f, 0.0129f)),
            new SpriteSpec("FrostPatriarch_StaggerHit", "B", new Rect(49f, 0f, 541f, 354f), new Vector2(0.4668f, 0.0113f)),
            new SpriteSpec("FrostPatriarch_KneelHold", "B", new Rect(689f, 0f, 542f, 278f), new Vector2(0.4741f, 0.0144f)),
            new SpriteSpec("FrostPatriarch_KneelRise", "B", new Rect(1372f, 0f, 456f, 337f), new Vector2(0.5154f, 0.0119f)),
        };

        private static readonly Color IceCyan = new Color(0.55f, 0.92f, 1f, 1f);
        private static readonly Color RiftMagenta = new Color(0.99f, 0.22f, 0.55f, 1f);

        // Body and footprint. The collider (Stats.Radius) is the trunk only; the paw/crystal reach of the poses never
        // inflates it (the view fits the IDLE sprite to it once, then every pose keeps that one scale).
        private const float BossRadius = 1.6f;

        // Icebreaker eruption line (shared by the delivery and its committed-lane telegraph).
        private const float EruptionRadius = 1.4f;
        // As many points as fit: the line runs until it meets a wall (the sealed arena edge included), a gap or another
        // floor - 12 x 2.7m covers any boss arena (30m).
        private const int EruptionCount = 12;
        private const float EruptionFirst = 3f;
        private const float EruptionSpacing = 2.7f;
        private const float EruptionFirstDelay = 0.6f;
        private const float EruptionInterval = 0.12f;   // fast: the line rolls out like a wave
        private const float EruptionWarningLead = 0.36f; // ~3 points warned ahead of the front - a short fuse, never the whole line


        // The three big mechanics share one high weight so they rotate on their cooldowns; the punch (weight 1) fills.
        private const int BigMechanicWeight = 4;
        // The jump slam (short engage range): above the big three, so whenever the target is in reach (e.g. right after
        // the leap lands on it) the bear actually uses it.
        private const int MeleeWeight = 5;
        // The jump slam's frost rings: 12 lines (30 deg) x 4 waves, 6m..13.5m.
        private const int FrostRingLines = 12;
        private const int FrostRingWaves = 4;
        private const float FrostRingFirstDelay = 0.6f;
        private const float FrostRingInterval = 0.3f; // = WarningLead: one ring telegraphed at a time, shown as the previous erupts

        // Polar Breath area - shared by the delivery, its telegraph and the breath effect's shape (FitBreathToCone).
        private const float BreathRange = 11f;   // long reach, short channel
        private const float BreathConeAngle = 60f; // = ConeTelegraph60
        private const float BreathMouthForward = 1.9f; // = breathDelivery.MouthOffset.z

        private static Dictionary<string, Sprite> _sprites;

        [MenuItem("Tools/RiftRaiders/Boss/Build Frost Patriarch (Arctic)")]
        public static void Build()
        {
            EnsureFolder(Root);
            EnsureFolder(VfxFolder);

            SliceSheets();
            Material bodyMaterial = EnsureBodyMaterial();

            // ---------------------------------------------------------------- owned effect variants
            ParticleSystem swipeSlash = BuildOwnedEffect(SlashPath, "FrostPatriarch_SwipeSlash", _ => new ParticleSystem.MinMaxGradient(RiftMagenta, new Color(1f, 0.75f, 0.9f, 1f)));
            ParticleSystem iceSlam = BuildOwnedEffect(ToonBodySlamPath, "FrostPatriarch_IceSlam", ps => ps.name switch
            {
                "CloudCircle" => new Color(0.66f, 0.84f, 1f, 0.5f), // snow kicked up - blue-shaded and translucent: reads on the snow floor without whiting out the boss
                "Sparks" => new Color(0.45f, 0.85f, 1f, 1f),
                _ => IceCyan,                                       // cold flash/wave
            });
            // The leap's landing wave: the same slam, its dust nearly gone - scaled to the 7m wave it would haze the arena.
            ParticleSystem iceWave = BuildOwnedEffect(ToonBodySlamPath, "FrostPatriarch_IceWave", ps => ps.name switch
            {
                "CloudCircle" => new Color(0.66f, 0.84f, 1f, 0.12f),
                "Sparks" => new Color(0.45f, 0.85f, 1f, 1f),
                _ => IceCyan,
            });
            // The pack slam is authored standing up - laid flat like Brute's RR_ToonBodySlam (root 270 X). Plays through
            // AreaHit (prefab rotation) as is; step particles pass the same rotation (Flat(...)).
            LayFlat(iceSlam);
            LayFlat(iceWave);
            // The W1 boss punch flash, in ice blue - the claw swipe's release.
            // Additive (the pack's own materials) and bright - it has to glow.
            ParticleSystem punchBlue = BuildOwnedEffect(PunchFlashPath, "FrostPatriarch_PunchFlash", _ => new ParticleSystem.MinMaxGradient(new Color(0.4f, 0.8f, 1f, 1f), new Color(0.85f, 0.97f, 1f, 1f)));
            ParticleSystem eruption = BuildOwnedEffect(PillarBlastPath, "FrostPatriarch_IceEruption", _ => new ParticleSystem.MinMaxGradient(IceCyan, Color.white));
            // Saturated ice blue, not white - it has to read over the Arctic's snow floor.
            ParticleSystem breath = BuildOwnedEffect(FlamethrowerPath, "FrostPatriarch_Breath", _ => new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.72f, 1f, 0.95f), new Color(0.66f, 0.9f, 1f, 0.95f)));
            FitBreathToCone(breath, BreathRange - BreathMouthForward, BreathConeAngle); // it starts at the mouth, the cone at the body centre

            Material shardMaterial = EnsureParticleMaterial($"{VfxFolder}/FrostPatriarch_Shard.mat", "Mobile/Particles/Alpha Blended", ShardTexturePath);
            Material snowMaterial = EnsureParticleMaterial($"{VfxFolder}/FrostPatriarch_Snow.mat", "Mobile/Particles/Alpha Blended", SnowTexturePath);
            Material mistMaterial = EnsureParticleMaterial($"{VfxFolder}/FrostPatriarch_Mist.mat", "Mobile/Particles/Alpha Blended", MistTexturePath);
            Material iceWall = EnsureIceWallMaterial();
            ConfigureEruptionSpikes(eruption, iceWall, shardMaterial, snowMaterial, mistMaterial);
            // Looping rumble for the eruption warnings (EruptionRumbleFxDirector stops it); every effect played as a plain
            // step particle must be one-shot - a looping one there is never stopped and stays on the map.
            ParticleSystem rumble = BuildEruptionRumble("FrostPatriarch_EruptionRumble", snowMaterial, shardMaterial, loop: true);
            ParticleSystem rumbleOnce = BuildEruptionRumble("FrostPatriarch_RumbleOnce", snowMaterial, shardMaterial, loop: false);
            ParticleSystem inhale = BuildFrostInhale(mistMaterial);

            EnsureCopy(W1BossPrefabPath, BossPrefabPath);
            RemoveRetiredChargeAssets();

            // ---------------------------------------------------------------- pass 1: every Quantum asset on disk first
            var boss = CloneOrLoad<BossDataAsset>(W1BossPath, "FrostPatriarch");

            var swipeTelegraph = CreateOrLoad<TelegraphData>("FrostPatriarch-SwipeTelegraph");
            var breathTelegraph = CreateOrLoad<TelegraphData>("FrostPatriarch-BreathTelegraph");

            var slamHit = CreateOrLoad<AreaHitData>("FrostPatriarch-SlamHit");
            var eruptionHit = CreateOrLoad<AreaHitData>("FrostPatriarch-EruptionHit");
            var chill = CreateOrLoad<ChillEffectData>("FrostPatriarch-BreathChill");
            var waveSlow = CreateOrLoad<ChillEffectData>("FrostPatriarch-LeapWaveChill");

            var swipe = CreateOrLoad<EnemyActionData>("FrostPatriarch-CorruptedSwipe");
            var swipeDelivery = CreateOrLoad<GroundAreaDeliveryData>("FrostPatriarch-CorruptedSwipeDelivery");
            var icebreaker = CreateOrLoad<EnemyActionData>("FrostPatriarch-Icebreaker");
            var icebreakerDelivery = CreateOrLoad<EruptionLineDeliveryData>("FrostPatriarch-IcebreakerDelivery");
            var icebreakerFan = CreateOrLoad<EnemyActionData>("FrostPatriarch-IcebreakerFan");
            var icebreakerFanDelivery = CreateOrLoad<EruptionLineDeliveryData>("FrostPatriarch-IcebreakerFanDelivery");

            var breathAction = CreateOrLoad<EnemyActionData>("FrostPatriarch-PolarBreath");
            var breathDelivery = CreateOrLoad<BreathConeDeliveryData>("FrostPatriarch-PolarBreathDelivery");
            var jumpSlam = CreateOrLoad<EnemyActionData>("FrostPatriarch-JumpSlam");
            var jumpSlamDelivery = CreateOrLoad<GroundAreaDeliveryData>("FrostPatriarch-JumpSlamDelivery");
            var jumpSlamTelegraph = CreateOrLoad<TelegraphData>("FrostPatriarch-JumpSlamTelegraph");
            var jumpSlamSequence = CreateOrLoad<SequenceDeliveryData>("FrostPatriarch-JumpSlamSequence");
            var jumpSlamHit = CreateOrLoad<EnemyActionData>("FrostPatriarch-JumpSlamHit");
            var frostRings = CreateOrLoad<EnemyActionData>("FrostPatriarch-FrostRings");
            var frostRingsDelivery = CreateOrLoad<EruptionLineDeliveryData>("FrostPatriarch-FrostRingsDelivery");
            var leap = CreateOrLoad<EnemyActionData>("FrostPatriarch-GlacialLeap");
            var leapDelivery = CreateOrLoad<LeapWaveDeliveryData>("FrostPatriarch-GlacialLeapDelivery");
            var kneel = CreateOrLoad<EnemyActionData>("FrostPatriarch-Kneel");
            var kneelDelivery = CreateOrLoad<AirborneKneelDeliveryData>("FrostPatriarch-KneelDelivery");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            EntityPrototype bossPrototype = LoadPrototype(BossPrefabPath);

            var damage = new AssetRef<HitEffectData>(AssetDatabase.LoadAssetAtPath<HitEffectData>(GenericDamagePath).Guid);
            var knockSmall = new AssetRef<HitEffectData>(AssetDatabase.LoadAssetAtPath<HitEffectData>(KnockbackSmallPath).Guid);
            var knockMedium = new AssetRef<HitEffectData>(AssetDatabase.LoadAssetAtPath<HitEffectData>(KnockbackMediumPath).Guid);
            var knockStrong = new AssetRef<HitEffectData>(AssetDatabase.LoadAssetAtPath<HitEffectData>(KnockbackStrongPath).Guid);

            ParticleSystem jumpPuff = LoadParticle(AssetDatabase.GUIDToAssetPath(JumpPuffGuid));
            GameObject cone90 = AssetDatabase.LoadAssetAtPath<GameObject>($"{TelegraphFolder}/ConeTelegraph90.prefab");
            GameObject cone60 = AssetDatabase.LoadAssetAtPath<GameObject>($"{TelegraphFolder}/ConeTelegraph60.prefab");
            GameObject square = AssetDatabase.LoadAssetAtPath<GameObject>($"{TelegraphFolder}/SquareTelegraph.prefab");
            GameObject circle = AssetDatabase.LoadAssetAtPath<GameObject>($"{TelegraphFolder}/CircleTelegraph.prefab");

            // ---------------------------------------------------------------- 1. Patada Corrompida (corrupted swipe)
            // A4 weight shifts, paw pulled back, cone shown; direction locks at 75% of the windup; A5 one cone sweep (one
            // hit per player - GroundAreaDelivery resolves once), small knockback, no cold; A6 paw out, flank open.
            swipeDelivery.ConeShaped = true;
            swipeDelivery.ConeAngleDegrees = 90; // = ConeTelegraph90
            swipeDelivery.SelfDestructs = false;
            swipeDelivery.MaxHeightDifference = F("1.2");
            ConeTelegraph(swipeTelegraph, cone90, 1f, 0.9f);

            // A long 8m cone (past the W1 boss punch's 6m) on a 5s cooldown.
            Action(swipe, "Patada Corrompida", engage: F("8.5"), damageRange: 8, damage: 30, anticipation: F("0.75"), telegraphAt: F("0.1"),
                downTime: F("0.8"), cooldown: F("5.0"), delivery: swipeDelivery, effects: new List<AssetRef<HitEffectData>> { damage, knockSmall });
            swipe.Origin = EnemyActionOrigin.Self;
            // Weight 1 (Action default) for now: a filler - the jump slam is the close-range answer.
            swipe.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            swipe.AimLock = EnemyAimLockTiming.LocksAtPercent;
            swipe.AimLockPercent = F("0.75");
            swipe.Telegraph = new AssetRef<TelegraphData>(swipeTelegraph.Guid);
            swipe.AnticipationStep = Step(AttackAnimationType.SwingBack, 0.9f, Sprite("FrostPatriarch_SwipePrep"));
            // Release: the magenta claw slash and the paw's impact inside the cone (snow + cold flash). Each
            // one sized from its measured particle reach to span the 8m cone (≈0.2-8m from the bear), not past it.
            swipe.BeginStep = Step(AttackAnimationType.PunchScale, 0.3f, Sprite("FrostPatriarch_SwipeRelease"),
                Directional(punchBlue, new Vector3(0f, -0.4f, 4.0f), 1.6f), shake: 2.2f, additional: new[]
                {
                    Directional(swipeSlash, new Vector3(0f, -0.4f, 4.3f), 1.55f),
                    Flat(iceSlam, new Vector3(0f, -1.4f, 4.1f), 1.35f),
                });
            swipe.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            swipe.EndStep = Step(AttackAnimationType.None, 1.0f, Sprite("FrostPatriarch_SwipeRecover")); // = DownTime: held, not snapped to idle

            // ---------------------------------------------------------------- 2. Quebra-Gelo (icebreaker)
            // A7 reared on the hind legs (a slam windup - no jump), the whole committed line is shown as a lane; A8 the
            // double-paw slam hits a short area in front, then three eruptions run down the locked line, each with its
            // own warning; A9 long recovery.
            slamHit.BlastRadius = F("2.2");
            slamHit.TargetMask = DamageTargetMask.Players;
            slamHit.MaxHeightDifference = F("1.2");
            slamHit.TriggersSpawnUpgrades = false;
            slamHit.DetonateOnEnemyHit = false;
            slamHit.Effects = new List<AssetRef<HitEffectData>> { damage, knockMedium };
            slamHit.BlastEffectPrefab = iceSlam;

            eruptionHit.BlastRadius = F(EruptionRadius.ToString(System.Globalization.CultureInfo.InvariantCulture));
            eruptionHit.TargetMask = DamageTargetMask.Players;
            eruptionHit.MaxHeightDifference = F("1.2");
            eruptionHit.TriggersSpawnUpgrades = false;
            eruptionHit.DetonateOnEnemyHit = false;
            eruptionHit.Effects = new List<AssetRef<HitEffectData>> { damage, knockSmall };
            eruptionHit.BlastEffectPrefab = eruption;

            icebreakerDelivery.SlamHit = new AssetRef<AreaHitData>(slamHit.Guid);
            icebreakerDelivery.SlamDistance = F("1.8");
            icebreakerDelivery.SlamDamage = 28;
            icebreakerDelivery.EruptionHit = new AssetRef<AreaHitData>(eruptionHit.Guid);
            icebreakerDelivery.EruptionCount = EruptionCount;
            icebreakerDelivery.FirstDistance = F(EruptionFirst.ToString(System.Globalization.CultureInfo.InvariantCulture));
            icebreakerDelivery.Spacing = F(EruptionSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            icebreakerDelivery.FirstDelay = F(EruptionFirstDelay.ToString(System.Globalization.CultureInfo.InvariantCulture));
            icebreakerDelivery.Interval = F(EruptionInterval.ToString(System.Globalization.CultureInfo.InvariantCulture));
            icebreakerDelivery.WarningLead = F(EruptionWarningLead.ToString(System.Globalization.CultureInfo.InvariantCulture));
            icebreakerDelivery.MaxHeightDifference = F("1.2");


            Action(icebreaker, "Quebra-Gelo", engage: 10, damageRange: F("2.2"), damage: 24, anticipation: F("0.95"), telegraphAt: F("0.1"),
                downTime: F("1.2"), cooldown: F("6.5"), delivery: icebreakerDelivery, effects: new List<AssetRef<HitEffectData>> { damage, knockSmall });
            icebreaker.SelectionWeight = BigMechanicWeight;
            icebreaker.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            icebreaker.AimLock = EnemyAimLockTiming.LocksAtPercent;
            icebreaker.AimLockPercent = F("0.7");
            icebreaker.Telegraph = default; // the per-eruption warnings are the telegraph - no windup lane
            icebreakerDelivery.WarningEffect = rumble;
            // Reared: frost drawn in around the raised paws, ice chips already jumping at its feet.
            icebreaker.AnticipationStep = Step(AttackAnimationType.Inflate, 1.1f, Sprite("FrostPatriarch_IcebreakerPrep"),
                Particle(inhale, new Vector3(0f, 1.2f, 0.4f), 1.8f), shake: 0.3f, additional: new[]
                {
                    Particle(rumbleOnce, new Vector3(0f, -1.6f, 0f), 1.6f, snap: true),
                });
            // Impact pose held for the whole eruption run (at most FirstDelay + Interval * (Count-1)) - EndStep takes over at recovery.
            icebreaker.BeginStep = Step(AttackAnimationType.Slam, EruptionFirstDelay + EruptionInterval * (EruptionCount - 1) + 0.1f, Sprite("FrostPatriarch_IcebreakerImpact"), shake: 2.5f);
            icebreaker.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            icebreaker.EndStep = Step(AttackAnimationType.None, 1.4f, Sprite("FrostPatriarch_IcebreakerRecover"));

            // Phase 2 version: three lines fanned 30 deg apart (covering the same 60 deg as ConeTelegraph60, shown during the
            // windup at the lines' reach), each wave 20% bigger than the last - the lines grow as they advance.
            CopyEruption(icebreakerDelivery, icebreakerFanDelivery);
            icebreakerFanDelivery.LineCount = 3;
            icebreakerFanDelivery.LineSpreadDegrees = 30;
            icebreakerFanDelivery.RadiusGrowthPerStep = F("0.05"); // gentle - the lines run to the arena edge

            Action(icebreakerFan, "Quebra-Gelo", engage: 10, damageRange: F("2.2"), damage: 24, anticipation: F("0.95"), telegraphAt: F("0.1"),
                downTime: F("1.2"), cooldown: F("6.5"), delivery: icebreakerFanDelivery, effects: icebreaker.Effects.ToList());
            icebreakerFan.SelectionWeight = BigMechanicWeight;
            icebreakerFan.Origin = EnemyActionOrigin.Self;
            icebreakerFan.DirectionTracking = icebreaker.DirectionTracking;
            icebreakerFan.AimLock = icebreaker.AimLock;
            icebreakerFan.AimLockPercent = icebreaker.AimLockPercent;
            icebreakerFan.Telegraph = default;
            icebreakerFan.AnticipationStep = icebreaker.AnticipationStep;
            icebreakerFan.BeginStep = icebreaker.BeginStep;
            icebreakerFan.OnGoingStep = icebreaker.OnGoingStep;
            icebreakerFan.EndStep = icebreaker.EndStep;

            // ---------------------------------------------------------------- 3. Sopro Polar (polar breath)
            // B1 chest swells, the cone is announced; aim locks at the end of the windup; B2 a short 1.5s, long (60 deg, 11m) channel turning at
            // most 35 deg/s toward the target - moderate damage every 0.25s and a fixed Chill per tick (first ticks slow,
            // ~5 ticks in it = Freeze, frozen heroes are skipped and get a recovery window); B3 head low, open to punish.
            chill.BuildupPerHit = F("1.0");
            chill.Duration = F("1.5");
            chill.PlayerFreezeRecovery = 3;

            breathDelivery.Duration = F("1.5");
            breathDelivery.TickInterval = F("0.25");
            breathDelivery.ConeAngleDegrees = (int)BreathConeAngle; // = ConeTelegraph60
            breathDelivery.TurnRateDegrees = 35;
            breathDelivery.MaxHeightDifference = F("1.2");
            breathDelivery.MouthHeight = FP._1;
            breathDelivery.SkipFrozenTargets = true;
            breathDelivery.BreathEffect = breath;
            breathDelivery.MouthOffset = new Vector3(0f, -0.7f, BreathMouthForward); // fallback only: from the body centre (radius above the floor)
            breathDelivery.MouthOnSprite = new Vector2(2.35f, 1.41f); // the open jaws on FrostPatriarch_BreathActive (sprite units from its pivot)
            breathDelivery.MouthFromSprite = true;
            breathDelivery.EffectScale = 1f;
            breathDelivery.AreaDecal = null; // the windup cone is the telegraph; once the breath starts only the frost shows
            ConeTelegraph(breathTelegraph, cone60, 1f, 0.9f);

            Action(breathAction, "Sopro Polar", engage: 8, damageRange: F(BreathRange.ToString(System.Globalization.CultureInfo.InvariantCulture)), damage: 7, anticipation: F("0.9"), telegraphAt: F("0.1"),
                downTime: F("1.0"), cooldown: 8, delivery: breathDelivery, effects: new List<AssetRef<HitEffectData>> { damage, new AssetRef<HitEffectData>(chill.Guid) });
            breathAction.SelectionWeight = BigMechanicWeight;
            breathAction.Origin = EnemyActionOrigin.Self;
            breathAction.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation; // the channel turns itself, rate-limited
            breathAction.AimLock = EnemyAimLockTiming.LocksAtTelegraphEnd;
            breathAction.Telegraph = new AssetRef<TelegraphData>(breathTelegraph.Guid);
            // Inhale: frost mist pulled into the mouth for the whole windup.
            breathAction.AnticipationStep = Step(AttackAnimationType.Inflate, 1.0f, Sprite("FrostPatriarch_BreathPrep"),
                Particle(inhale, new Vector3(0f, -0.5f, 1.7f), 1.2f));
            breathAction.BeginStep = Step(AttackAnimationType.None, 1.5f, Sprite("FrostPatriarch_BreathActive"), shake: 0.4f); // = Duration
            breathAction.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            breathAction.EndStep = Step(AttackAnimationType.None, 1.2f, Sprite("FrostPatriarch_BreathRecover"));

            // ---------------------------------------------------------------- 4. Salto Glacial (glacial leap)
            // B4 coiled with ice chips at the paws, the landing circle shown on the target; the spot locks at 75% of the
            // windup and is pulled back onto real floor (ResolveValidLanding); B5 stretched through the air; touchdown =
            // landing hit + a horizontal frost shockwave the size of the slow wave (the same ToonBodySlam family Brute's
            // Groundbreaker landing uses, in ice) and heavy shake; the wave slows (non-Ice StickySlow, never
            // Chill/Freeze) every player it sweeps over; B6 recovery.
            // The slow is the game's own Chill, so heroes get its usual feedback (blue tint, frost particles) - capped so
            // the wave alone can never Freeze (4 of the 5 threshold = ~32% slow), refreshed for 2s.
            waveSlow.BuildupPerHit = 4;
            waveSlow.MaxBuildup = 4;
            waveSlow.Duration = 2;
            waveSlow.PlayerFreezeRecovery = 3;


            leapDelivery.JumpDuration = F("0.85"); // real hang time in the air
            leapDelivery.JumpHeight = 10; // as high as the W1 boss leap
            leapDelivery.MaxHeightDifference = F("1.2");
            leapDelivery.ResolveValidLanding = true;
            leapDelivery.MaxLandingHeightDifference = F("1.5");
            leapDelivery.WaveEffects = new List<AssetRef<HitEffectData>> { new AssetRef<HitEffectData>(waveSlow.Guid) };
            leapDelivery.WaveRadius = 7;
            leapDelivery.WaveSpeed = 14; // the wave stage holds the bear only ~0.5s - the landing chains into a punch/slam
            leapDelivery.WaveMaxHeightDifference = F("1.2");
            leapDelivery.WaveVisual = default; // the landing slam's own flat shockwave IS the wave (see the Wave stage step)
            leapDelivery.WaveHeightOffset = F("0.1");
            leapDelivery.StageSteps = new[]
            {
                // Air: stretched out over the arena.
                Step(AttackAnimationType.None, 0.7f, Sprite("FrostPatriarch_LeapJump")),
                // Wave (= touchdown): forepaws planted, a flat frost shockwave out to the wave's 7m + the cold impact at the
                // landing hit's size, heavy shake.
                Step(AttackAnimationType.Slam, 0.8f, Sprite("FrostPatriarch_LeapLand"), Flat(iceWave, new Vector3(0f, -1.4f, 0f), 7f), shake: 3f,
                    additional: new[]
                    {
                        Flat(iceSlam, new Vector3(0f, -1.4f, 0f), 4f),
                        Particle(LoadParticle(GroundCrackPath), new Vector3(0f, -1.4f, 0f), 3.5f, snap: true), // the cracked floor, as on the W1/Neon bosses
                    }),
            };

            leapDelivery.WarnLandingInAir = true; // the landing telegraph appears at takeoff, not during the windup

            // DamageRange = the landing hit (body radius 1.6 + 1.6).
            Action(leap, "Salto Glacial", engage: 14, damageRange: 4, damage: 36, anticipation: F("0.65"), telegraphAt: F("0.1"),
                downTime: F("0.2"), cooldown: F("5.5"), delivery: leapDelivery, effects: new List<AssetRef<HitEffectData>> { damage, knockStrong });
            leap.SelectionWeight = BigMechanicWeight;
            leap.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            leap.AimLock = EnemyAimLockTiming.LocksAtPercent;
            leap.AimLockPercent = F("0.9"); // tracks the target almost to takeoff
            leap.Telegraph = default;
            leap.AnticipationStep = Step(AttackAnimationType.Crouch, 0.9f, Sprite("FrostPatriarch_LeapPrep"),
                Particle(rumbleOnce, new Vector3(0f, -1.6f, 0f), 1.4f, snap: true));
            leap.BeginStep = Step(AttackAnimationType.None, 0.1f, null, Particle(jumpPuff, Vector3.zero, 3f, snap: true));
            leap.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            // Almost no recovery: the leap is a gap closer - it lands on the (now chilled) target and the melee weight makes
            // the punch / jump slam the next pick (Leap -> Punch, Leap -> Slam combos).
            leap.EndStep = Step(AttackAnimationType.None, 0.25f, Sprite("FrostPatriarch_IcebreakerRecover")); // = DownTime

            // ---------------------------------------------------------------- 5. Pisão Glacial (jump slam)
            // Close range, alternating with the punch: B4 crouch, then a hop in place over the windup (B5 in the air), landing
            // on the hit - a ground slam ALL around the bear (5m, circle telegraph under it), floor-gated, with the flat ice
            // shockwave, the cracked floor and a heavy shake - then FROST RINGS: the Icebreaker's ice pyramids (same
            // eruption, warnings, rumble) erupting in concentric rings that march outward in waves. 12 lines 30 deg apart
            // = the rings; the first is nearly closed, the outer ones open lanes between spikes. Counterplay: step IN
            // towards the bear (the slam is over) or into a lane. SequenceDeliveryData: step 1 the slam (JumpSlamHit),
            // step 2 the rings (FrostRings).
            jumpSlamDelivery.ConeShaped = false;
            jumpSlamDelivery.SelfDestructs = false;
            jumpSlamDelivery.MaxHeightDifference = F("1.2");
            CircleTelegraph(jumpSlamTelegraph, circle, 1f, 0.8f);

            Action(jumpSlamHit, "Pisão Glacial - slam", engage: F("5.5"), damageRange: 5, damage: 28, anticipation: FP._0, telegraphAt: FP._0,
                downTime: FP._0, cooldown: FP._0, delivery: jumpSlamDelivery, effects: new List<AssetRef<HitEffectData>> { damage, knockMedium });
            jumpSlamHit.Origin = EnemyActionOrigin.Self;

            CopyEruption(icebreakerDelivery, frostRingsDelivery);
            frostRingsDelivery.SlamHit = default; // the jump slam already hit
            frostRingsDelivery.SlamDamage = FP._0;
            frostRingsDelivery.LineCount = FrostRingLines;
            frostRingsDelivery.LineSpreadDegrees = 360 / FrostRingLines;
            frostRingsDelivery.EruptionCount = FrostRingWaves;
            frostRingsDelivery.FirstDistance = 6;
            frostRingsDelivery.Spacing = F("2.5");
            frostRingsDelivery.FirstDelay = F(FrostRingFirstDelay.ToString(System.Globalization.CultureInfo.InvariantCulture));
            frostRingsDelivery.Interval = F(FrostRingInterval.ToString(System.Globalization.CultureInfo.InvariantCulture));
            frostRingsDelivery.WarningLead = frostRingsDelivery.Interval;
            frostRingsDelivery.RadiusGrowthPerStep = F("0.1");
            Action(frostRings, "Pisão Glacial - frost rings", engage: F("5.5"), damageRange: 6, damage: 18, anticipation: FP._0, telegraphAt: FP._0,
                downTime: FP._0, cooldown: FP._0, delivery: frostRingsDelivery, effects: new List<AssetRef<HitEffectData>>());

            jumpSlamSequence.Steps = new List<AssetRef<EnemyActionData>>
            {
                new AssetRef<EnemyActionData>(jumpSlamHit.Guid),
                new AssetRef<EnemyActionData>(frostRings.Guid),
            };

            Action(jumpSlam, "Pisão Glacial", engage: F("5.5"), damageRange: 5, damage: 28, anticipation: F("0.8"), telegraphAt: F("0.05"),
                downTime: F("0.8"), cooldown: F("3.5"), delivery: jumpSlamSequence, effects: new List<AssetRef<HitEffectData>> { damage, knockMedium });
            jumpSlam.Origin = EnemyActionOrigin.Self;
            jumpSlam.SelectionWeight = MeleeWeight;
            jumpSlam.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            jumpSlam.Telegraph = new AssetRef<TelegraphData>(jumpSlamTelegraph.Guid);
            jumpSlam.AnticipationStep = Step(AttackAnimationType.Jump, 0.8f, Sprite("FrostPatriarch_LeapJump"), Particle(jumpPuff, Vector3.zero, 2.5f, snap: true));
            jumpSlam.AnticipationStep.Jump = new JumpParams { Height = 2.5f, LandSquash = 0.35f };
            // Landing pose held over the whole ring run (= the frost rings' last wave), then EndStep at recovery.
            jumpSlam.BeginStep = Step(AttackAnimationType.Slam, FrostRingFirstDelay + FrostRingInterval * (FrostRingWaves - 1) + 0.1f, Sprite("FrostPatriarch_LeapLand"), Flat(iceWave, new Vector3(0f, -1.4f, 0f), 5f), shake: 2.6f,
                additional: new[]
                {
                    Flat(iceSlam, new Vector3(0f, -1.4f, 0f), 2.6f),
                    Particle(LoadParticle(GroundCrackPath), new Vector3(0f, -1.4f, 0f), 2.6f, snap: true),
                });
            jumpSlam.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            jumpSlam.EndStep = Step(AttackAnimationType.None, 0.8f, Sprite("FrostPatriarch_IcebreakerRecover")); // = DownTime

            // ---------------------------------------------------------------- Kneel (stagger break, forced only)
            // B7 loses balance, B8 drops on the corrupted shoulder - the Rupture (exposure) window is exactly this pose -
            // B9 pushes back up. Begin cancels its running mechanics (breath/eruptions via preemption) with no final damage;
            // a break during the leap waits for it to land (DeferForcedActionsWhileActive).
            kneelDelivery.HitTime = F("0.4");
            kneelDelivery.FallSpeed = 9;
            kneelDelivery.MinFallTime = F("0.35");
            kneelDelivery.Duration = F("3.5");
            kneelDelivery.DamageMultiplier = F("1.5");
            kneelDelivery.RiseTime = F("0.7");
            kneelDelivery.AirborneThreshold = F("0.6");
            kneelDelivery.CancelOwnedHazards = true;
            kneelDelivery.StageSteps = new[]
            {
                Step(AttackAnimationType.Shake, 0.4f, Sprite("FrostPatriarch_StaggerHit"), shake: 1f), // Hit
                Step(AttackAnimationType.None, 0.5f, Sprite("FrostPatriarch_StaggerHit")),                                                       // Fall (never airborne - unused)
                Step(AttackAnimationType.None, 3.5f, Sprite("FrostPatriarch_KneelHold"), Flat(iceSlam, Vector3.zero, 2f), shake: 1.5f), // Kneel
                Step(AttackAnimationType.None, 0.7f, Sprite("FrostPatriarch_KneelRise")),                                                       // Rise
            };

            Action(kneel, "Kneel", engage: FP._0, damageRange: 2, damage: FP._0, anticipation: FP._0, telegraphAt: FP._1,
                downTime: F("0.3"), cooldown: FP._1, delivery: kneelDelivery, effects: new List<AssetRef<HitEffectData>>());
            kneel.Telegraph = default;
            kneel.AnticipationStep = Step(AttackAnimationType.None, 0.1f, null);
            kneel.BeginStep = Step(AttackAnimationType.None, 0.1f, null);
            kneel.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            kneel.EndStep = Step(AttackAnimationType.None, 0.1f, null);

            // ---------------------------------------------------------------- the boss
            BossDataAsset w1Boss = AssetDatabase.LoadAssetAtPath<BossDataAsset>(W1BossPath);
            boss.EnemyName = "Patriarca da Geada";
            boss.Title = "PATRIARCA DA GEADA";
            boss.Subtitle = "O Urso da Fenda";
            boss.UiSprite = Sprite("FrostPatriarch_Idle");
            boss.Description = "A Rift-corrupted polar bear. Swipes with a crystal paw, smashes the ice into erupting lines, freezes you with its breath and charges through anything.";
            boss.CatalogSubtitle = "O Urso da Fenda";
            boss.ExplosionColor = RiftMagenta;
            boss.Stats.MoveSpeed = F("5.6");
            boss.Stats.Radius = F(BossRadius.ToString(System.Globalization.CultureInfo.InvariantCulture));
            boss.Stats.HealthMultiplier = F("1.6");
            boss.Actions.BasicAction = default;
            boss.Actions.SkillActions = new List<AssetRef<EnemyActionData>>
            {
                new AssetRef<EnemyActionData>(swipe.Guid),        // 0 - slot 1
                new AssetRef<EnemyActionData>(icebreaker.Guid),   // 1 - slot 2
                new AssetRef<EnemyActionData>(breathAction.Guid), // 2 - slot 3
                new AssetRef<EnemyActionData>(leap.Guid),         // 3 - slot 4
                new AssetRef<EnemyActionData>(kneel.Guid),        // 4 - slot 5, forced only (stagger break)
                new AssetRef<EnemyActionData>(icebreakerFan.Guid), // 5 - slot 6, phase 2's Icebreaker
                new AssetRef<EnemyActionData>(jumpSlam.Guid),     // 6 - slot 7, close-range jump slam
            };
            boss.Passives = new List<AssetRef<EnemyPassiveData>>();
            // Swipe/breath/leap in both phases; the Icebreaker is per phase - a straight line, then the three-line fan.
            boss.GlobalActionSlots = new List<int> { 0, 2, 3, 6 };
            boss.ComboChains = new List<BossComboChainData>();
            boss.DeferForcedActionsWhileActive = true;     // a break mid-delivery waits - except the ones that allow preemption
            boss.FallBackWhenTopActionBlocked = true;
            boss.DebugPrototype = new AssetRef<EntityPrototype>(bossPrototype.Guid);

            StaggerProfileData stagger = w1Boss.Stagger;
            stagger.ThresholdPercents = w1Boss.Stagger.ThresholdPercents.ToList();
            stagger.OnBreakForcedAction = new AssetRef<EnemyActionData>(kneel.Guid);
            boss.Stagger = stagger;

            // Same kit and sprites in both phases. Phase 2 (<= 50%): a little faster on its feet and quicker to recover;
            // windups (= reaction time) untouched.
            boss.Phases = new List<BossPhaseData>
            {
                new BossPhaseData { EntryTrigger = BossPhaseEntryTrigger.HealthThreshold, HealthPercentThreshold = FP._1, ActionPoolSlots = new List<int> { 1 } },
                new BossPhaseData
                {
                    EntryTrigger = BossPhaseEntryTrigger.HealthThreshold,
                    HealthPercentThreshold = FP._0_50,
                    ActionPoolSlots = new List<int> { 5 },
                    Modifiers = new BossStatModifiers
                    {
                        RecoveryMultiplier = F("1.4"),
                        MoveSpeedMultiplier = F("1.2"),
                        AnticipationMultiplier = F("1.1"),
                    },
                },
            };

            foreach (Object asset in new Object[] { icebreakerFan, icebreakerFanDelivery, jumpSlam, jumpSlamDelivery, jumpSlamTelegraph, jumpSlamSequence,
                         jumpSlamHit, frostRings, frostRingsDelivery })
                EditorUtility.SetDirty(asset);

            foreach (Object asset in new Object[] { boss, swipeTelegraph, breathTelegraph, slamHit, eruptionHit, chill,
                         waveSlow, swipe, swipeDelivery, icebreaker, icebreakerDelivery, breathAction, breathDelivery, leap, leapDelivery, kneel, kneelDelivery })
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            RemoveUnusedEffectMaterials();
            ConfigureBossPrefab(bodyMaterial, boss);
            AssetDatabase.SaveAssets();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            Debug.Log($"[FrostPatriarch] Built Frost Patriarch - boss {boss.Guid}, prototype {bossPrototype.Guid}");
        }

        // ------------------------------------------------------------------ sprites / materials

        private static void SliceSheets()
        {
            Slice(SheetAPath, "A", 100);
            Slice(SheetBPath, "B", 100);

            _sprites = new Dictionary<string, Sprite>();
            foreach (string path in new[] { SheetAPath, SheetBPath })
            {
                foreach (Sprite sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                    _sprites[sprite.name] = sprite;
            }
        }

        private static void Slice(string path, string sheet, float ppu)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;

#pragma warning disable CS0618 // legacy spritesheet API - same as RiffRiotBossBuilder / DonoDoCeuBossBuilder
            importer.spritesheet = Sprites.Where(s => s.Sheet == sheet).Select(s => new SpriteMetaData
            {
                name = s.Name,
                rect = s.Rect,
                alignment = (int)SpriteAlignment.Custom,
                pivot = s.Pivot,
            }).ToArray();
#pragma warning restore CS0618

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private static Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out Sprite sprite) == false)
                throw new System.InvalidOperationException($"[FrostPatriarch] sprite {name} not found");

            return sprite;
        }

        // Own copy of the W1 boss's pink-crystal glow sprite material (same shader) - the Rift crystals glow the same way.
        private static Material EnsureBodyMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            if (existing != null)
                return existing;

            GameObject w1 = AssetDatabase.LoadAssetAtPath<GameObject>(W1BossPrefabPath);
            Material source = w1.GetComponentsInChildren<SpriteRenderer>(true).Select(r => r.sharedMaterial).First(m => m != null && m.name == W1BossMaterialName);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), BodyMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        }

        private static Material EnsureParticleMaterial(string path, string shader, string texturePath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find(shader);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ effects

        // Owned copy of a pack effect (the original stays untouched), each system re-tinted. Pack materials on
        // Particles/Standard Unlit (which URP doesn't draw) or other built-ins get an owned Mobile/Particles material on
        // the same texture (_ADD = additive, else alpha blended); a rerun keeps the already-owned ones.
        private static ParticleSystem BuildOwnedEffect(string sourcePath, string name, System.Func<ParticleSystem, ParticleSystem.MinMaxGradient> tint)
        {
            string path = $"{VfxFolder}/{name}.prefab";
            EnsureCopy(sourcePath, path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                // Decided from the PACK's material of the same renderer (by hierarchy path), so a rerun re-evaluates it:
                // a material that already renders in URP is used as is (shared, untouched - keeps its additive glow);
                // only Particles/Standard Unlit (no URP pass) gets an owned Mobile/Particles copy on the same texture,
                // additive when the source is.
                GameObject sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    string relative = RelativePath(root.transform, renderer.transform);
                    Transform sourceTransform = relative.Length == 0 ? sourceRoot.transform : sourceRoot.transform.Find(relative);
                    Material source = sourceTransform != null && sourceTransform.TryGetComponent(out ParticleSystemRenderer sourceRenderer) ? sourceRenderer.sharedMaterial : null;
                    if (source == null)
                        continue;

                    if (source.shader.name.StartsWith("Particles/Standard") == false && source.shader.isSupported == true)
                    {
                        renderer.sharedMaterial = source;
                        continue;
                    }

                    bool additive = source.name.ToUpperInvariant().Contains("ADD") || source.shader.name.Contains("Additive") ||
                                    source.IsKeywordEnabled("_BLENDMODE_ADD") == true;
                    string shader = additive ? "Mobile/Particles/Additive" : "Mobile/Particles/Alpha Blended";
                    string prefix = name.Replace("FrostPatriarch_", "");
                    renderer.sharedMaterial = EnsureParticleMaterial($"{VfxFolder}/FrostPatriarch_{prefix}_{source.name}.mat", shader, AssetDatabase.GetAssetPath(source.mainTexture));
                }

                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.startColor = tint(ps);
                    WhitenColorOverLifetime(ps);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
        }

        // The eruption burst becomes a real 3D ice spike: a holder counter-rotated to the burst's authored rotation (so it is
        // world-upright when EffectsManager plays the burst), two clusters of the boss arena wall's pyramid spikes in ice
        // (variants), a snow burst on the hit, a lingering frost mist (keeps the pooled instance alive for the whole
        // animation) and the shard burst IceEruptionSpikeView emits when the spike shatters. The burst is played at the
        // eruption radius (local units x 1.4 = world).
        private const float SpikeSinkDepth = 2.3f;

        // One pyramid of a cluster: base position on the floor (local x/z), base width, height, outward tilt in degrees.
        private readonly struct SpikePart
        {
            public readonly float X, Z, Width, Height, Tilt;

            public SpikePart(float x, float z, float width, float height, float tilt)
            {
                X = x;
                Z = z;
                Width = width;
                Height = height;
                Tilt = tilt;
            }
        }

        // A tall central spike crowned by smaller ones leaning outward - the arena wall's jagged look, as a burst.
        private static readonly SpikePart[] ClusterA =
        {
            new SpikePart(0f, 0f, 0.9f, 2.1f, 0f),
            new SpikePart(0.62f, 0.15f, 0.55f, 1.3f, 18f),
            new SpikePart(-0.58f, 0.22f, 0.5f, 1.1f, 20f),
            new SpikePart(0.18f, -0.6f, 0.48f, 1.0f, 22f),
            new SpikePart(-0.32f, -0.5f, 0.36f, 0.75f, 26f),
            new SpikePart(0.45f, 0.62f, 0.3f, 0.6f, 30f),
        };

        private static readonly SpikePart[] ClusterB =
        {
            new SpikePart(0.1f, 0.05f, 0.8f, 1.8f, 6f),
            new SpikePart(-0.35f, -0.1f, 0.62f, 1.45f, 14f),
            new SpikePart(0.6f, -0.35f, 0.45f, 1.0f, 24f),
            new SpikePart(-0.15f, 0.62f, 0.42f, 0.9f, 24f),
            new SpikePart(0.55f, 0.48f, 0.32f, 0.65f, 30f),
        };

        // Own copy of the arena wall material (same rim shader), tinted ice instead of Rift pink.
        private static Material EnsureIceWallMaterial()
        {
            string path = $"{VfxFolder}/FrostPatriarch_IceWall.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                AssetDatabase.CopyAsset(RiftWallMaterialPath, path);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            material.SetColor("_TintColor", new Color(0.42f, 0.84f, 1f, 1f));
            material.SetColor("_RimColor", new Color(1f, 1f, 1f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureEruptionSpikes(ParticleSystem burst, Material ice, Material shard, Material snow, Material mist)
        {
            string path = AssetDatabase.GetAssetPath(burst);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                Transform holder = EnsureChild(root.transform, "SpikeHolder");
                holder.localPosition = Vector3.zero;
                holder.localRotation = Quaternion.Inverse(root.transform.localRotation);
                holder.localScale = Vector3.one;

                Mesh pyramid = AssetDatabase.LoadAllAssetsAtPath(PyramidMeshPath).OfType<Mesh>().First();
                Transform a = EnsureCluster(holder, "SpikeA", ClusterA, pyramid, ice);
                Transform b = EnsureCluster(holder, "SpikeB", ClusterB, pyramid, ice);

                ParticleSystem shatter = EnsureChildSystem(holder, "Shatter");
                ConfigureBurst(shatter, count: 0, lifetime: new Vector2(0.55f, 0.9f), speed: new Vector2(4f, 8f), size: new Vector2(0.18f, 0.4f), gravity: 2.4f);
                ParticleSystem.EmissionModule shatterEmission = shatter.emission;
                shatterEmission.enabled = false; // emitted by IceEruptionSpikeView at the shatter, never on Play
                ParticleSystem.MainModule shatterMain = shatter.main;
                shatterMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.94f, 1f, 1f), Color.white);
                ParticleSystem.ShapeModule shatterShape = shatter.shape;
                shatterShape.enabled = true;
                shatterShape.shapeType = ParticleSystemShapeType.Sphere;
                shatterShape.radius = 0.5f;
                shatter.transform.localPosition = new Vector3(0f, 1f, 0f);
                SheetAndMaterial(shatter, shard);

                ParticleSystem snowBurst = EnsureChildSystem(holder, "SnowBurst");
                ConfigureBurst(snowBurst, count: 16, lifetime: new Vector2(0.5f, 0.85f), speed: new Vector2(3f, 6.5f), size: new Vector2(0.25f, 0.6f), gravity: 1.6f);
                SetStartColor(snowBurst, new Color(0.74f, 0.87f, 1f, 1f));
                ParticleSystem.ShapeModule snowShape = snowBurst.shape;
                snowShape.enabled = true;
                snowShape.shapeType = ParticleSystemShapeType.Cone;
                snowShape.angle = 35f;
                snowShape.radius = 0.5f;
                snowBurst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                SheetAndMaterial(snowBurst, snow);

                ParticleSystem frostMist = EnsureChildSystem(holder, "FrostMist");
                ConfigureBurst(frostMist, count: 6, lifetime: new Vector2(1.25f, 1.45f), speed: new Vector2(0.2f, 0.7f), size: new Vector2(1.2f, 2f), gravity: -0.05f);
                SetStartColor(frostMist, new Color(0.7f, 0.88f, 1f, 0.3f));
                ParticleSystem.ShapeModule mistShape = frostMist.shape;
                mistShape.enabled = true;
                mistShape.shapeType = ParticleSystemShapeType.Circle;
                mistShape.radius = 0.8f;
                frostMist.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                ParticleSystem.ColorOverLifetimeModule mistColor = frostMist.colorOverLifetime;
                mistColor.enabled = true;
                mistColor.color = Fade(0.15f, 0.55f);
                ParticleSystem.SizeOverLifetimeModule mistSize = frostMist.sizeOverLifetime;
                mistSize.enabled = true;
                mistSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.2f));
                SheetAndMaterial(frostMist, mist);

                var view = GetOrAdd<IceEruptionSpikeView>(root);
                var so = new SerializedObject(view);
                SerializedProperty variants = so.FindProperty("variants");
                variants.arraySize = 2;
                variants.GetArrayElementAtIndex(0).objectReferenceValue = a;
                variants.GetArrayElementAtIndex(1).objectReferenceValue = b;
                so.FindProperty("shatter").objectReferenceValue = shatter;
                so.FindProperty("sinkDepth").floatValue = SpikeSinkDepth;
                so.FindProperty("shakeImpact").floatValue = 1.2f;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Rebuilt from the layout on every run (a previous model/cluster under the same name is replaced).
        private static Transform EnsureCluster(Transform parent, string name, SpikePart[] parts, Mesh pyramid, Material ice)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var cluster = new GameObject(name).transform;
            cluster.SetParent(parent, false);

            for (int i = 0; i < parts.Length; i++)
            {
                SpikePart part = parts[i];
                var spike = new GameObject($"Spike{i}", typeof(MeshFilter), typeof(MeshRenderer));
                spike.transform.SetParent(cluster, false);
                spike.transform.localPosition = new Vector3(part.X, 0f, part.Z);

                Vector3 outward = new Vector3(part.X, 0f, part.Z);
                spike.transform.localRotation = outward.sqrMagnitude > 0.0001f && part.Tilt > 0f
                    ? Quaternion.AngleAxis(part.Tilt, Vector3.Cross(Vector3.up, outward.normalized)) * Quaternion.Euler(0f, 45f * i, 0f)
                    : Quaternion.Euler(0f, 45f * i, 0f);
                spike.transform.localScale = new Vector3(part.Width, part.Height, part.Width);

                spike.GetComponent<MeshFilter>().sharedMesh = pyramid;
                var renderer = spike.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = ice;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            cluster.gameObject.SetActive(false);
            return cluster;
        }

        // Played on every eruption point for its warning countdown (EruptionRumbleFxDirector, scaled by the eruption
        // radius): ice chips and snow hopping off the cracking floor.
        private static ParticleSystem BuildEruptionRumble(string name, Material snow, Material shard, bool loop)
        {
            return CreateParticlePrefab(name, root =>
            {
                ParticleSystem chips = root.GetComponent<ParticleSystem>();
                ConfigureLoop(chips, rate: 26f, lifetime: 0.45f, size: new Vector2(0.08f, 0.2f));
                ParticleSystem.MainModule main = chips.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
                main.gravityModifier = 1.8f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.94f, 1f, 1f), Color.white);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.ShapeModule shape = chips.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.7f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // flat on the floor, emitting up - spawners ignore the root's rotation
                root.transform.localRotation = Quaternion.identity;
                SheetAndMaterial(chips, shard);

                ParticleSystem dust = EnsureChildSystem(root.transform, "SnowDust");
                ConfigureLoop(dust, rate: 10f, lifetime: 0.6f, size: new Vector2(0.25f, 0.5f));
                ParticleSystem.MainModule dustMain = dust.main;
                dustMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                dustMain.simulationSpace = ParticleSystemSimulationSpace.World;
                dustMain.startColor = new Color(0.74f, 0.87f, 1f, 0.8f);
                ParticleSystem.ShapeModule dustShape = dust.shape;
                dustShape.enabled = true;
                dustShape.shapeType = ParticleSystemShapeType.Circle;
                dustShape.radius = 0.8f;
                dustShape.rotation = new Vector3(-90f, 0f, 0f);
                ParticleSystem.ColorOverLifetimeModule dustColor = dust.colorOverLifetime;
                dustColor.enabled = true;
                dustColor.color = Fade(0.1f, 0.5f);
                SheetAndMaterial(dust, snow);

                if (loop == false)
                {
                    MakeOneShot(chips, 0.9f);
                    MakeOneShot(dust, 0.9f);
                }
            });
        }

        // Frost mist pulled inward (negative speed on a sphere) - the breath's inhale and the Icebreaker's gathering cold.
        private static ParticleSystem BuildFrostInhale(Material mist)
        {
            return CreateParticlePrefab("FrostPatriarch_FrostInhale", root =>
            {
                ParticleSystem ps = root.GetComponent<ParticleSystem>();
                ConfigureLoop(ps, rate: 30f, lifetime: 0.5f, size: new Vector2(0.25f, 0.55f));
                ParticleSystem.MainModule main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(-3.2f, -2.4f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.94f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.9f));
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 1.4f;
                ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
                color.enabled = true;
                color.color = Fade(0.25f, 0.8f);
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                SheetAndMaterial(ps, mist);
                MakeOneShot(ps, 0.9f); // windup-only step particle: must end by itself
            });
        }

        // Emits for `duration` then stops - EffectsManager releases it once the last particle dies.
        private static void MakeOneShot(ParticleSystem ps, float duration)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.duration = duration;
        }

        private static ParticleSystem CreateParticlePrefab(string name, System.Action<GameObject> configure)
        {
            string path = $"{VfxFolder}/{name}.prefab";
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(name, typeof(ParticleSystem));
            bool loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

            try
            {
                configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (loaded)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
        }

        private static void ConfigureBurst(ParticleSystem ps, int count, Vector2 lifetime, Vector2 speed, Vector2 size, float gravity)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 64;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(count > 0 ? new[] { new ParticleSystem.Burst(0f, (short)count) } : new ParticleSystem.Burst[0]);
        }

        // 2x2 sheet, random frame, on its owned alpha-blended material.
        private static void SheetAndMaterial(ParticleSystem ps, Material material)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static ParticleSystem EnsureChildSystem(Transform parent, string name)
        {
            Transform child = EnsureChild(parent, name);
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            return ps != null ? ps : child.gameObject.AddComponent<ParticleSystem>();
        }

        private static void ConfigureLoop(ParticleSystem ps, float rate, float lifetime, Vector2 size)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = lifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 128;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
        }

        private static Gradient Fade(float inUntil, float outFrom)
        {
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inUntil), new GradientAlphaKey(1f, outFrom), new GradientAlphaKey(0f, 1f) });
            return fade;
        }

        // The pack flamethrower is a narrow jet (2-7 deg cone); the breath must fill exactly the hit area - a coneAngle wedge
        // reaching range. Every layer's emission cone opens to the half angle and speed x lifetime is set to the range, with
        // big particles so the far end of the fan is covered (the effect itself is played at scale 1).
        private static void FitBreathToCone(ParticleSystem breath, float range, float coneAngle)
        {
            string path = AssetDatabase.GetAssetPath(breath);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    // (speed, lifetime, size, rate) per layer; StartFire (the mouth flash) keeps its own.
                    (Vector2 speed, Vector2 life, Vector2 size, float rate)? layer = ps.name switch
                    {
                        "Glow" => (new Vector2(30f, 34f), new Vector2(9f, 9f), new Vector2(1.4f, 2.2f), 200f),
                        "Smoke" => (new Vector2(26f, 30f), new Vector2(9f, 9f), new Vector2(1.2f, 2f), 140f),
                        "Embers" => (new Vector2(24f, 30f), new Vector2(9f, 9f), new Vector2(0.07f, 0.12f), 120f),
                        "StartFire" => null,
                        _ => (new Vector2(30f, 34f), new Vector2(9f, 9f), new Vector2(1.6f, 2.4f), 180f), // the main stream (fast)
                    };

                    if (layer == null)
                        continue;

                    // Lifetime from speed so every particle ends at the arc (not short of it): the spray fills the wedge.
                    var (speed, life, size, rate) = layer.Value;
                    float reach = range / ((speed.x + speed.y) * 0.5f);
                    ParticleSystem.MainModule main = ps.main;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
                    life = new Vector2(reach * 0.95f, reach * 1.05f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);

                    // Grow toward the far end so the wide part of the wedge is covered.
                    ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
                    grow.enabled = true;
                    grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));
                    main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);

                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.rateOverTime = rate;
                    main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(rate * life.y * 1.5f)); // never capped below its own density

                    ParticleSystem.ShapeModule shape = ps.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = coneAngle * 0.5f;
                    shape.radius = 0.3f;

                    // Nothing slows them before the range (the pack's drag/limit would cut the reach short).
                    ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
                    limit.enabled = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // The Glacial Charge (and its ice trail) was replaced by the Glacial Leap - its generated assets go with it.
        private static void RemoveRetiredChargeAssets()
        {
            foreach (string path in new[]
                     {
                         PathOf("FrostPatriarch-GlacialCharge"), PathOf("FrostPatriarch-GlacialChargeDelivery"), PathOf("FrostPatriarch-ChargeTelegraph"),
                         PathOf("FrostPatriarch-IceTrail"), PathOf("FrostPatriarch-IceTrailSlow"),
                         RetiredIceTrailPrefabPath, RetiredIceTrailPrefabPath.Replace(".prefab", "EntityPrototype.qprototype"),
                         $"{VfxFolder}/FrostPatriarch_IceSplash.prefab", $"{VfxFolder}/FrostPatriarch_IceSparkle.prefab", $"{VfxFolder}/FrostPatriarch_SnowKick.prefab",
                         // first leap landing look (vertical snow splat + ring line), replaced by the flat shockwave
                         $"{VfxFolder}/FrostPatriarch_SnowSplat.prefab", $"{VfxFolder}/FrostPatriarch_WaveRing.prefab", PathOf("FrostPatriarch-LeapWaveVisual"),
                         // the Icebreaker's windup telegraphs (the per-eruption warnings are enough)
                         PathOf("FrostPatriarch-IcebreakerTelegraph"), PathOf("FrostPatriarch-IcebreakerFanTelegraph"),
                         PathOf("FrostPatriarch-LeapWaveSlow"), // the wave's StickySlow had no visible feedback on heroes - now Chill
                         PathOf("FrostPatriarch-LeapTelegraph"), // the windup circle - the leap now warns its landing once airborne
                         // the jump slam's snowball ring, replaced by the frost rings
                         PathOf("FrostPatriarch-SnowballRing"), PathOf("FrostPatriarch-SnowballRingDelivery"), PathOf("FrostPatriarch-SnowballProjectile"),
                         PathOf("FrostPatriarch-SnowballMovement"), PathOf("FrostPatriarch-SnowballHit"), RetiredProjectileFolder,
                         $"{VfxFolder}/FrostPatriarch_SnowballBullet.mat", $"{VfxFolder}/FrostPatriarch_Snowball.mat",
                     })
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }

            foreach (string guid in AssetDatabase.FindAssets("FrostPatriarch_SnowSplat_ t:Material", new[] { VfxFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        // Owned particle materials (VfxFolder) that no effect prefab of this boss uses any more - e.g. copies made before a
        // pack material that renders fine in URP was reused as is.
        private static void RemoveUnusedEffectMaterials()
        {
            var used = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxFolder }))
                used.UnionWith(AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(guid), true));
            used.UnionWith(AssetDatabase.GetDependencies(BossPrefabPath, true));

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { VfxFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path) == false && System.IO.Path.GetFileName(path).StartsWith("FrostPatriarch_"))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static string RelativePath(Transform root, Transform child)
        {
            string path = "";
            for (Transform t = child; t != null && t != root; t = t.parent)
                path = path.Length == 0 ? t.name : $"{t.name}/{path}";
            return path;
        }

        private static void ConfigureBossPrefab(Material material, BossDataAsset boss)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);

            try
            {
                EnemyViewRig rig = root.GetComponentInChildren<EnemyViewRig>(true);
                rig.gameObject.name = "FrostPatriarch-Body";

                var renderer = rig.GetComponent<SpriteRenderer>();
                renderer.sprite = Sprite("FrostPatriarch_Idle");
                renderer.sharedMaterial = material;

                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("dyingSprite").objectReferenceValue = Sprite("FrostPatriarch_KneelHold");
                SerializedProperty walk = rigSo.FindProperty("walkSprites");
                walk.arraySize = 2;
                walk.GetArrayElementAtIndex(0).objectReferenceValue = Sprite("FrostPatriarch_WalkA");
                walk.GetArrayElementAtIndex(1).objectReferenceValue = Sprite("FrostPatriarch_WalkB");
                rigSo.FindProperty("walkFramesPerStride").floatValue = 2f;
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                // The elite pinkish-red smoke, as on the W1 boss: copied from its rig if a previous build removed it, then
                // placed at the same spot of this body (76% up the sprite) and the same size relative to it.
                Transform smoke = rig.transform.Find(W1SmokeChild);
                if (smoke == null)
                {
                    GameObject w1 = PrefabUtility.LoadPrefabContents(W1BossPrefabPath);
                    try
                    {
                        Transform source = w1.GetComponentInChildren<EnemyViewRig>(true).transform.Find(W1SmokeChild);
                        smoke = Object.Instantiate(source.gameObject, rig.transform).transform;
                        smoke.name = W1SmokeChild;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(w1);
                    }
                }

                smoke.localPosition = new Vector3(0f, 2.1f, 0f);
                smoke.localRotation = Quaternion.identity;
                smoke.localScale = Vector3.one * 1.65f;

                GetOrAdd<QPrototypeEnemySequenceState>(root); // the jump slam is a SequenceDeliveryData (slam + frost rings)

                var enemyProto = root.GetComponent<QPrototypeEnemy>();
                var so = new SerializedObject(enemyProto);
                SerializedProperty data = so.FindProperty("Prototype.EnemyData.Id.Value");
                if (data == null)
                    throw new System.InvalidOperationException("[FrostPatriarch] QPrototypeEnemy.Prototype.EnemyData not found - serialized layout changed");

                data.longValue = (long)boss.Guid.Value;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void CopyEruption(EruptionLineDeliveryData from, EruptionLineDeliveryData to)
        {
            to.SlamHit = from.SlamHit;
            to.SlamDistance = from.SlamDistance;
            to.SlamDamage = from.SlamDamage;
            to.EruptionHit = from.EruptionHit;
            to.EruptionCount = from.EruptionCount;
            to.FirstDistance = from.FirstDistance;
            to.Spacing = from.Spacing;
            to.FirstDelay = from.FirstDelay;
            to.Interval = from.Interval;
            to.WarningLead = from.WarningLead;
            to.MaxHeightDifference = from.MaxHeightDifference;
            to.WarningEffect = from.WarningEffect;
        }

        private static EntityPrototype LoadPrototype(string prefabPath)
        {
            string protoPath = prefabPath.Replace(".prefab", "EntityPrototype.qprototype");
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            EntityPrototype prototype = AssetDatabase.LoadAllAssetsAtPath(protoPath).OfType<EntityPrototype>().FirstOrDefault();

            if (prototype == null || prototype.Guid.IsValid == false)
                throw new System.InvalidOperationException($"[FrostPatriarch] no EntityPrototype generated for {prefabPath} (expected {protoPath})");

            return prototype;
        }

        private static string PathOf(string name) => $"{Root}/{name}.asset";

        private static T CreateOrLoad<T>(string name) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(PathOf(name));
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, PathOf(name));
            return asset;
        }

        private static T CloneOrLoad<T>(string templatePath, string name) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(PathOf(name));
            if (existing != null)
                return existing;

            // Identifier reset so the importer stamps a fresh Quantum guid on the copy.
            T clone = Object.Instantiate(AssetDatabase.LoadAllAssetsAtPath(templatePath).OfType<T>().First());
            clone.name = name;
            clone.Identifier = default;
            AssetDatabase.CreateAsset(clone, PathOf(name));
            return clone;
        }

        private static void EnsureCopy(string from, string to)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null)
                return;

            if (AssetDatabase.CopyAsset(from, to) == false)
                throw new System.InvalidOperationException($"[FrostPatriarch] could not copy {from} -> {to}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // Not `GetComponent() ?? AddComponent()` - in the Editor a missing component comes back as Unity's fake null,
        // which `??` doesn't treat as null.
        private static T GetOrAdd<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }

        private static void SetStartColor(ParticleSystem ps, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = color;
        }

        // Keeps the alpha curve, drops any baked hue so start colour alone decides it.
        private static void WhitenColorOverLifetime(ParticleSystem ps)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            if (colorOverLifetime.enabled == false || colorOverLifetime.color.gradient == null)
                return;

            var white = new Gradient();
            white.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, colorOverLifetime.color.gradient.alphaKeys);
            colorOverLifetime.color = white;
        }

        private static void ConeTelegraph(TelegraphData telegraph, GameObject prefab, float radiusMultiplier, float growth)
        {
            telegraph.Shape = TelegraphShape.Cone;
            telegraph.StartPhase = AttackPhase.Anticipation;
            telegraph.EndPhase = AttackPhase.Begin;
            telegraph.LiveTracking = true;
            telegraph.SnapToGround = true;
            telegraph.RadiusMultiplier = radiusMultiplier;
            telegraph.GrowthDuration = growth;
            telegraph.TelegraphPrefab = prefab;
        }

        // Circle on the (tracked, then locked) target anchor - the landing spot, same setup as the W1 boss leap telegraph.
        private static void CircleTelegraph(TelegraphData telegraph, GameObject prefab, float radiusMultiplier, float growth)
        {
            telegraph.Shape = TelegraphShape.Circle;
            telegraph.StartPhase = AttackPhase.Anticipation;
            telegraph.EndPhase = AttackPhase.Begin;
            telegraph.LiveTracking = true;
            telegraph.SnapToGround = true;
            telegraph.RadiusMultiplier = radiusMultiplier;
            telegraph.GrowthDuration = growth;
            telegraph.TelegraphPrefab = prefab;
        }

        // Fixed-length lane from the boss along its aim - the same setup as the W1 boss charge telegraph.
        private static void LaneTelegraph(TelegraphData telegraph, GameObject prefab, float length, float width)
        {
            telegraph.Shape = TelegraphShape.Rectangle;
            telegraph.StartPhase = AttackPhase.Anticipation;
            telegraph.EndPhase = AttackPhase.Begin;
            telegraph.LiveTracking = true;
            telegraph.SnapToGround = true;
            telegraph.LineLength = TelegraphLineLength.FixedDistance;
            telegraph.FixedDistanceValue = length;
            telegraph.FromOffset = 0f;
            telegraph.ToOffset = 0f;
            telegraph.Width = width;
            telegraph.GrowthDuration = 1f;
            telegraph.TelegraphPrefab = prefab;
        }

        private static ParticleSystem LoadParticle(string path)
        {
            var particle = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
            if (particle == null)
                Debug.LogWarning($"[FrostPatriarch] particle prefab missing at {path} - step left without it");

            return particle;
        }

        private static FP F(string value) => FP.FromString(value);

        private static void Action(EnemyActionData action, string name, FP engage, FP damageRange, FP damage, FP anticipation, FP telegraphAt,
            FP downTime, FP cooldown, EnemyDeliveryData delivery, List<AssetRef<HitEffectData>> effects)
        {
            action.Name = name;
            action.EngageRange = engage;
            action.DamageRange = damageRange;
            action.Damage = damage;
            action.AnticipationTime = anticipation;
            action.TelegraphStartPercent = telegraphAt;
            action.DownTime = downTime;
            action.CooldownTime = cooldown;
            action.IgnoreY = true;
            action.Origin = EnemyActionOrigin.TargetAnchor;
            action.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionWhileActive;
            action.AimLock = EnemyAimLockTiming.LocksAtTelegraphEnd;
            action.AimLockPercent = FP._1;
            action.SelectionWeight = 1;
            action.InterruptibleDuringTelegraph = false; // a boss windup is a promise - knockback never cancels it
            action.InterruptibleDuringActive = false;
            action.Trigger = default;
            action.Delivery = new AssetRef<EnemyDeliveryData>(delivery.Guid);
            action.Effects = effects;
        }

        private static AttackVisualStep Step(AttackAnimationType animation, float duration, Sprite sprite, AttackVisualParticle particle = null, float shake = 0f,
            AttackVisualParticle[] additional = null)
        {
            var step = new AttackVisualStep
            {
                AnimationType = animation,
                Duration = duration,
                BodySprite = sprite,
                ShakeImpact = shake,
                AdditionalParticles = additional ?? new AttackVisualParticle[0],
            };

            if (particle != null)
            {
                step.ParticlePrefab = particle.ParticlePrefab;
                step.Anchor = particle.Anchor;
                step.Offset = particle.Offset;
                step.SnapToGround = particle.SnapToGround;
                step.Parented = particle.Parented;
                step.Scale = particle.Scale;
                step.AlignToEnemyDirection = particle.AlignToEnemyDirection;
                step.RotationOffset = particle.RotationOffset;
            }

            return step;
        }

        private static AttackVisualParticle Particle(ParticleSystem prefab, Vector3 offset, float scale, bool snap = false)
        {
            return new AttackVisualParticle { ParticlePrefab = prefab, Anchor = ParticleAnchor.OnSelf, Offset = offset, SnapToGround = snap, Scale = scale };
        }

        // A pack slam laid flat for a step particle (the step spawner ignores the prefab's own rotation).
        private static AttackVisualParticle Flat(ParticleSystem prefab, Vector3 offset, float scale)
        {
            AttackVisualParticle particle = Particle(prefab, offset, scale, snap: true);
            particle.RotationOffset = new Vector3(270f, 0f, 0f);
            return particle;
        }

        private static void LayFlat(ParticleSystem effect)
        {
            string path = AssetDatabase.GetAssetPath(effect);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                root.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Rotated to the boss's aim (same muzzle-flash setup as the W1 boss cone punch).
        private static AttackVisualParticle Directional(ParticleSystem prefab, Vector3 offset, float scale)
        {
            AttackVisualParticle particle = Particle(prefab, offset, scale);
            particle.AlignToEnemyDirection = true;
            particle.RotationOffset = new Vector3(0f, 270f, -15f);
            return particle;
        }
    }
}
