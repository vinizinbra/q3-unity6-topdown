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
    /// Builds the Neon boss Riff Riot (docs/riff-riot-boss.md) - sprites, view/entity prefabs and every Quantum asset -
    /// from the approved sheet. Idempotent: re-running keeps every asset (and so every guid) and re-applies the tuning
    /// below, so THIS file is the source of truth for the numbers - hand edits to the generated assets are overwritten.
    ///
    /// Every asset is its own file with its own Quantum guid; the W1 boss / W1 Gunner / W2 Scarab Nest are only read as
    /// templates (Object.Instantiate + a reset Identifier), never modified.
    /// </summary>
    public static class RiffRiotBossBuilder
    {
        private const string SheetPath = "Assets/_Project/Art/Sprites/Enemy/Neon/RiffRiot/RiffRiotSheet.png";
        private const string MaterialPath = "Assets/_Project/Art/Sprites/Enemy/Neon/RiffRiot/RiffRiot_PinkCrystalGlow.mat";
        private const string Root = "Assets/_QuantumUser/Resources/Enemy/Neon/Boss/RiffRiot";
        private const string BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/Neon-RiffRiotPrefab.prefab";
        private const string ProjectilePrefabPath = "Assets/_QuantumUser/Entities/Projectile/RiffRiotSoundProjectile.prefab";
        private const string VfxFolder = "Assets/_Project/Prefabs/Effects/RiffRiot";
        // Same Epic Toon FX library the rest of the game's VFX are built from (the Rift Pink boss blast uses ring_ADD and
        // circlestretchy_AB too) - only referenced, never modified.
        // Hand-authored replacement for the Power Chord hit VFX. When this prefab exists the builder wires it instead of
        // its own generated RiffRiot_SoundWaveCone (which it regenerates every run) - and never touches it.
        private const string PowerChordFxOverridePath = "Assets/_Project/Prefabs/Effects/RiffRiot/RiffRiot_PowerChordFx.prefab";
        private const string CrescentTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/wave4.png";
        private const string SoftCrescentTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/wave3.png";
        private const string GlowTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/glow1.png";
        private const string StretchyTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/circlestretchy.png";
        private const string RingMaterialPath = "Assets/3rd-party/Epic Toon FX/Materials/Basics/ring_ADD.mat";
        private const string SpeakerViewPath = "Assets/_Project/Prefabs/View/EnemyView/Neon/RiffRiotSpeaker.prefab";
        // Distorted-guitar SFX generated from two reference takes (Source~/riff_riot_guitar_sfx.py) - clips + their SoundData.
        private const string GuitarClipFolder = "Assets/_Project/Audio/Library/RiffRiot";
        private const string SoundFolder = "Assets/_Project/Audio/Enemies";

        // Templates (read only).
        private const string W1BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/World1BossPrefab.prefab";
        private const string W1BossPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss.asset";
        private const string W1KneelPath = "Assets/_QuantumUser/Resources/Kneel.asset";
        private const string W1PunchDeliveryPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss-GroundPunchDelivery.asset";
        private const string W1PunchTelegraphPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss-ConePunchTelegraph.asset";
        private const string Cone90TelegraphPrefabPath = "Assets/_QuantumUser/Resources/QuantumViewAssets/Telegraph/ConeTelegraph90.prefab";
        private const string W1LeapTelegraphPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss-lLeapTelegraph.asset";
        private const string W1GunnerPath = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies/W1-Gunner.asset";
        private const string W2ScarabNestPath = "Assets/_QuantumUser/Resources/Enemy/World2/Wildlife/ScarabNest/W2-ScarabNest.asset";
        private const string EnemyProjectilePrefabPath = "Assets/_QuantumUser/Entities/Projectile/BasicEnemyWeaponProjectileEntity.prefab";
        private const string GenericDirectHitPath = "Assets/_QuantumUser/Resources/Weapons/Projectile/GenericDirectHitData 1.asset";
        private const string W1BossMaterialName = "SpriteSelectiveColorGlow_PinkCrystal";

        // Shared Rift Pink / impact VFX (same ones the boss intro/defeat cinematics use).
        private const string PinkExplosionPath = "Assets/_Project/Resources/BossDefeat/BossDefeatExplosion.prefab";
        private const string CrystalSparksPath = "Assets/_Project/Resources/BossDefeat/BossDefeatCrystalSparks.prefab";
        private const string PinkMuzzlePath = "Assets/3rd-party/CartoonVFX by Wallcoeur/VFX_Bundle_Wallcoeur/01_PREFAB/3D_Simple_MuzzleFlash/MuzzleFlash_Pink (3).prefab";
        private const string BodySlamPath = "Assets/_Project/Prefabs/Effects/RR_ToonBodySlam.prefab";
        private const string JumpPuffGuid = "dbe2ea8610622df44a4a79ba99af5ddc";     // W1 boss leap take-off puff
        private const string LandingDustGuid = "45530878ee0a04b94a334de43117ba46";  // W1 boss leap landing extra

        // The cleaned sheet (RiffRiotSheet.png, generated from Assets/0_Refs/NeonBoss/RiffRiotSpriteSheet-v2.png:
        // each pose isolated from its neighbours, glow halo removed, nothing redrawn). Pivot = boot baseline, centred on
        // the vest, so the body never jumps between poses; the airborne pose uses its tucked boots, so the jump height
        // comes only from the simulated arc.
        private readonly struct SpriteSpec
        {
            public readonly string Name;
            public readonly Rect Rect;
            public readonly Vector2 Pivot;

            public SpriteSpec(string name, Rect rect, Vector2 pivot)
            {
                Name = name;
                Rect = rect;
                Pivot = pivot;
            }
        }

        private static readonly SpriteSpec[] Sprites =
        {
            new SpriteSpec("RiffRiot_Idle", new Rect(12f, 774f, 487f, 372f), new Vector2(0.4230f, 0.0161f)),
            new SpriteSpec("RiffRiot_PowerChord", new Rect(534f, 775f, 467f, 371f), new Vector2(0.4604f, 0.0162f)),
            new SpriteSpec("RiffRiot_LeapPrep", new Rect(1043f, 783f, 473f, 354f), new Vector2(0.4947f, 0.0226f)),
            new SpriteSpec("RiffRiot_LeapAir", new Rect(57f, 404f, 398f, 345f), new Vector2(0.5201f, 0.0203f)),
            new SpriteSpec("RiffRiot_LeapImpact", new Rect(552f, 405f, 431f, 343f), new Vector2(0.5081f, 0.0525f)),
            new SpriteSpec("RiffRiot_LeapRecover", new Rect(1063f, 405f, 434f, 343f), new Vector2(0.4954f, 0.0496f)),
            new SpriteSpec("RiffRiot_Stagger", new Rect(22f, 34f, 468f, 317f), new Vector2(0.4444f, 0.0189f)),
            new SpriteSpec("RiffRiot_Roar", new Rect(534f, 29f, 467f, 326f), new Vector2(0.3961f, 0.0184f)),
            new SpriteSpec("RiffRiot_Speaker", new Rect(1125f, 28f, 310f, 329f), new Vector2(0.5000f, 0.0213f)),
        };

        private const float PowerChordRange = 6.5f;

        private static readonly Color RiffPink = new Color(1f, 0.18f, 0.6f, 1f);

        private static Dictionary<string, Sprite> _sprites;

        [MenuItem("Tools/RiftRaiders/Boss/Build Riff Riot (Neon)")]
        public static void Build()
        {
            SliceSheet();
            Material material = EnsureMaterial();
            EnsureFolder(Root);
            EnsureFolder("Assets/_Project/Prefabs/View/EnemyView/Neon");

            GameObject speakerView = BuildSpeakerView(material);
            EnsureCopy(W1BossPrefabPath, BossPrefabPath);
            EnsureCopy(EnemyProjectilePrefabPath, ProjectilePrefabPath);
            TintProjectileView();

            // Pass 1: every Quantum asset exists on disk (so the importer stamps its guid) before anything references it.
            var boss = CloneOrLoad<BossDataAsset>(W1BossPath, "RiffRiot");
            var speaker = CloneOrLoad<EnemyDataAsset>(W2ScarabNestPath, "RiffRiot-Speaker");
            var speakerMovement = CloneSubOrLoad<StationaryMovementData>(W2ScarabNestPath, "RiffRiot-SpeakerMovement");
            var soundProjectile = CloneSubOrLoad<ProjectileDataAsset>(W1GunnerPath, "RiffRiot-SoundProjectile");
            var soundMovement = CloneSubOrLoad<StraightProjectileMovementData>(W1GunnerPath, "RiffRiot-SoundProjectileMovement");
            // Shared generic direct-hit library asset (the W1 Gunner uses it too) - only referenced, never modified.
            var soundHit = AssetDatabase.LoadAssetAtPath<ProjectileHitData>(GenericDirectHitPath);
            var kneel = CloneOrLoad<EnemyActionData>(W1KneelPath, "RiffRiot-Kneel");
            var leapTelegraph = CloneOrLoad<TelegraphData>(W1LeapTelegraphPath, "RiffRiot-StageBreakerTelegraph");

            var powerChord = CreateOrLoad<EnemyActionData>("RiffRiot-PowerChord");
            // Power Chord is a cone slam (W1 boss ground punch shape) - the first build made it a projectile fan.
            AssetDatabase.DeleteAsset(PathOf("RiffRiot-PowerChordDelivery"));
            var powerChordDelivery = CloneOrLoad<GroundAreaDeliveryData>(W1PunchDeliveryPath, "RiffRiot-PowerChordCone");
            var powerChordTelegraph = CloneOrLoad<TelegraphData>(W1PunchTelegraphPath, "RiffRiot-PowerChordTelegraph");
            var leap = CreateOrLoad<EnemyActionData>("RiffRiot-StageBreaker");
            var leapDelivery = CreateOrLoad<LeapDeliveryData>("RiffRiot-StageBreakerDelivery");
            var stage = CreateOrLoad<EnemyActionData>("RiffRiot-SomNoTalo");
            var stageDelivery = CreateOrLoad<SpeakerStageDeliveryData>("RiffRiot-SomNoTaloDelivery");
            var kneelDelivery = CreateOrLoad<KneelDeliveryData>("RiffRiot-KneelDelivery");
            var roar = CreateOrLoad<EnemyActionData>("RiffRiot-Roar");
            var roarDelivery = CreateOrLoad<HoldPoseDeliveryData>("RiffRiot-RoarDelivery");
            var pulse = CreateOrLoad<EnemyActionData>("RiffRiot-SpeakerPulse");
            var pulseDelivery = CreateOrLoad<GapRingProjectileDeliveryData>("RiffRiot-SpeakerPulseDelivery");
            var pulseTelegraph = CreateOrLoad<TelegraphData>("RiffRiot-SpeakerPulseTelegraph");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            EntityPrototype projectilePrototype = LoadPrototype(ProjectilePrefabPath);
            EntityPrototype bossPrototype = LoadPrototype(BossPrefabPath);

            // W1 boss leap's own hit effects (generic damage + strong knockback) - shared effect library, only referenced.
            BossDataAsset w1Boss = AssetDatabase.LoadAssetAtPath<BossDataAsset>(W1BossPath);
            EnemyActionData w1Leap = AssetDatabase.LoadAllAssetsAtPath(W1BossPath).OfType<EnemyActionData>().First(a => a.name == "World1Boss-LeapAttack");
            List<AssetRef<HitEffectData>> leapEffects = w1Leap.Effects.ToList();
            // Pellets: the damage effect only - a knockback per sound pellet would juggle heroes through a ring.
            List<AssetRef<HitEffectData>> projectileEffects = leapEffects.Where(e => QuantumUnityDB.GetGlobalAssetEditorInstance(e) is DamageEffectData).ToList();

            ParticleSystem pinkBlast = LoadParticle(PinkExplosionPath);
            ParticleSystem crystals = LoadParticle(CrystalSparksPath);
            ParticleSystem pinkMuzzle = LoadParticle(PinkMuzzlePath);
            EnsureFolder(VfxFolder);
            // Sound waves: crescents riding the Power Chord cone out to its exact DamageRange, and a flat expanding ring
            // for the speakers' pulse / the roar.
            ParticleSystem waveCone = BuildSoundCone("RiffRiot_SoundWaveCone", PowerChordRange);
            ParticleSystem waveRing = BuildSoundRing("RiffRiot_SoundWaveRing", 2.4f);
            ParticleSystem bodySlam = LoadParticle(BodySlamPath);
            ParticleSystem jumpPuff = LoadParticle(AssetDatabase.GUIDToAssetPath(JumpPuffGuid));
            ParticleSystem landingDust = LoadParticle(AssetDatabase.GUIDToAssetPath(LandingDustGuid));

            // ---------------------------------------------------------------- sound projectile
            soundMovement.Speed = FP._1;
            soundProjectile.Prototype = new AssetRef<EntityPrototype>(projectilePrototype.Guid);
            soundProjectile.Movement = new AssetRef<ProjectileMovementData>(soundMovement.Guid);
            soundProjectile.Hit = new AssetRef<ProjectileHitData>(soundHit.Guid);
            soundProjectile.Lifetime = 4;
            soundProjectile.MaxDistance = 6;
            // Sphere sweep instead of the enemy bullet's infinitely-thin raycast - matches the energy orb's visible body
            // (white core ~0.26, halo ~1.2). Kept under ~0.3 so a shot flying at body height never scrapes the floor.
            soundProjectile.HitRadius = F("0.28");

            // ---------------------------------------------------------------- 1. Power Chord (directional cone slam)
            // 90 deg cone so the shared ConeTelegraph90 decal is exactly the area that gets hit. Floor-height gated, so a
            // hero on a ledge above/below isn't caught by a chord played on the arena floor.
            powerChordDelivery.ConeShaped = true;
            powerChordDelivery.ConeAngleDegrees = 90;
            powerChordDelivery.SelfDestructs = false;
            powerChordDelivery.MaxHeightDifference = F("1.2");

            powerChordTelegraph.Shape = TelegraphShape.Cone;
            powerChordTelegraph.StartPhase = AttackPhase.Anticipation;
            powerChordTelegraph.EndPhase = AttackPhase.Begin;
            powerChordTelegraph.LiveTracking = true;   // follows the aim, frozen once it locks
            powerChordTelegraph.SnapToGround = true;
            powerChordTelegraph.RadiusMultiplier = 1f;
            powerChordTelegraph.GrowthDuration = 0.85f;
            powerChordTelegraph.TelegraphPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Cone90TelegraphPrefabPath);

            Action(powerChord, "Power Chord", engage: 6, damageRange: FP.FromFloat_UNSAFE(PowerChordRange), damage: 30, anticipation: F("0.85"), telegraphAt: F("0.2"),
                downTime: F("0.75"), cooldown: F("2.5"), delivery: powerChordDelivery, effects: leapEffects);
            powerChord.Origin = EnemyActionOrigin.Self;
            powerChord.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            powerChord.AimLock = EnemyAimLockTiming.LocksAtPercent;
            powerChord.AimLockPercent = F("0.6");      // ~0.35s of locked cone before the chord lands
            powerChord.Telegraph = new AssetRef<TelegraphData>(powerChordTelegraph.Guid);
            // Wind-up reuses the fist-up pose; the strum pose holds through the recovery.
            powerChord.AnticipationStep = Step(AttackAnimationType.SwingBack, 0.85f, Sprite("RiffRiot_Roar"));
            // Neon sound waves sweep the exact cone (their own glow flash marks the guitar), turned to the aim.
            ParticleSystem chordFx = AssetDatabase.LoadAssetAtPath<ParticleSystem>(PowerChordFxOverridePath) ?? waveCone;
            AttackVisualParticle waves = Extra(chordFx, new Vector3(0f, 0f, 0.4f), 1f, snap: true);
            waves.AlignToEnemyDirection = true;
            powerChord.BeginStep = Step(AttackAnimationType.PunchScale, 0.75f, Sprite("RiffRiot_PowerChord"), waves, shake: 1.2f);
            powerChord.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);
            powerChord.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- 2. Quebra-Palco (leap)
            leapDelivery.JumpDuration = F("0.9");
            leapDelivery.JumpHeight = F("4.5");
            leapDelivery.MaxHeightDifference = F("1.2");
            leapDelivery.ResolveValidLanding = true;
            leapDelivery.MaxLandingHeightDifference = F("1.5");

            leapTelegraph.Shape = TelegraphShape.Circle;
            leapTelegraph.StartPhase = AttackPhase.Anticipation;   // crouch: mark appears and follows...
            leapTelegraph.EndPhase = AttackPhase.End;              // ...until the guitar hits the ground
            leapTelegraph.LiveTracking = true;                     // follows SkillTargetPosition - frozen once the aim locks
            leapTelegraph.SnapToGround = true;
            leapTelegraph.RadiusMultiplier = 1f;
            leapTelegraph.GrowthDuration = 1.8f;                   // fills up over windup + air time

            Action(leap, "Quebra-Palco", engage: 14, damageRange: F("3.4"), damage: 34, anticipation: F("0.9"), telegraphAt: F("0.15"),
                downTime: F("1.1"), cooldown: F("5.5"), delivery: leapDelivery, effects: leapEffects);
            leap.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation; // never homes once airborne
            leap.AimLock = EnemyAimLockTiming.LocksAtPercent;
            leap.AimLockPercent = F("0.55");                       // locked ~0.4s before take-off + 0.9s of air time
            leap.Telegraph = new AssetRef<TelegraphData>(leapTelegraph.Guid);
            leap.AnticipationStep = Step(AttackAnimationType.Crouch, 0.9f, Sprite("RiffRiot_LeapPrep"));
            leap.BeginStep = Step(AttackAnimationType.None, 0.9f, Sprite("RiffRiot_LeapAir"),
                Particle(jumpPuff, Vector3.zero, 4f, snap: true));
            leap.OnGoingStep = Step(AttackAnimationType.None, 0.9f, Sprite("RiffRiot_LeapAir"));
            // Impact, then the "pulling the guitar out of the floor" pose for the rest of the recovery.
            leap.EndStep = Step(AttackAnimationType.PunchScale, 1.3f, null,
                Particle(bodySlam, new Vector3(0f, 0.1f, 0f), 4f, anchor: ParticleAnchor.SkillTargetPosition), shake: 4f,
                additional: new[]
                {
                    Extra(pinkBlast, Vector3.zero, 1.6f, snap: true),
                    Extra(landingDust, new Vector3(0f, -1.3f, 0f), 3f),
                    Extra(crystals, Vector3.zero, 0.35f, snap: true),
                });
            leap.EndStep.BodySpriteFrames = new[] { Sprite("RiffRiot_LeapImpact"), Sprite("RiffRiot_LeapRecover"), Sprite("RiffRiot_LeapRecover") };

            // ---------------------------------------------------------------- 3. Som no Talo (speakers)
            pulseDelivery.ProjectileData = new AssetRef<ProjectileDataAsset>(soundProjectile.Guid);
            pulseDelivery.ProjectileSpeedMultiplier = F("6.5");
            pulseDelivery.ProjectileMaxDistanceMultiplier = F("2.2");     // ~13u
            pulseDelivery.PelletCount = 22;
            pulseDelivery.GapDegrees = 80;
            pulseDelivery.GapJitterDegrees = 60;

            pulseTelegraph.Shape = TelegraphShape.Circle;
            pulseTelegraph.StartPhase = AttackPhase.Anticipation;
            pulseTelegraph.EndPhase = AttackPhase.Begin;
            pulseTelegraph.LiveTracking = false;
            pulseTelegraph.SnapToGround = true;
            pulseTelegraph.RadiusMultiplier = 1f;
            pulseTelegraph.GrowthDuration = 0.8f;
            pulseTelegraph.TelegraphPrefab = leapTelegraph.TelegraphPrefab;

            // EngageRange 0: the speaker never picks this itself - BossSpeakerStageSystem forces it, alternating.
            Action(pulse, "Speaker Pulse", engage: 0, damageRange: F("1.6"), damage: 15, anticipation: F("0.8"), telegraphAt: FP._0,
                downTime: F("0.2"), cooldown: FP._0, delivery: pulseDelivery, effects: projectileEffects);
            pulse.DirectionTracking = DirectionUpdateMode.DoNotUpdateTargetDirection;
            pulse.Origin = EnemyActionOrigin.Self;
            pulse.Telegraph = new AssetRef<TelegraphData>(pulseTelegraph.Guid);
            pulse.AnticipationStep = Step(AttackAnimationType.Pulse, 0.8f, null);
            pulse.AnticipationStep.Pulse.Frequency = 9f;
            pulse.AnticipationStep.Pulse.MaxSquash = 0.18f;
            pulse.BeginStep = Step(AttackAnimationType.PunchScale, 0.3f, null,
                Particle(waveRing, Vector3.zero, 1f, snap: true), shake: 0.4f,
                additional: new[] { Extra(pinkBlast, new Vector3(0f, 0.8f, 0f), 1.1f) });
            pulse.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- guitar SFX (EnemyActionSoundTable)
            // Power chord on the slam, dive bomb on take-off (it spans the 0.9s air time), heavy chord on landing,
            // palm-muted chugs (a riff walked in order) on each speaker pulse.
            EnemyActionSoundTableAuthoring.Set(powerChord, AttackPhase.Begin, GuitarSound("RiffRiotPowerChord", 0.85f, 0.4f, 2));
            EnemyActionSoundTableAuthoring.Set(leap, AttackPhase.Begin, GuitarSound("RiffRiotDiveBomb", 0.7f, 0.5f, 1));
            EnemyActionSoundTableAuthoring.Set(leap, AttackPhase.End, GuitarSound("RiffRiotLandingChord", 0.9f, 0.5f, 1));
            EnemyActionSoundTableAuthoring.Set(pulse, AttackPhase.Begin,
                GuitarSound("RiffRiotAmpChug", 0.6f, 0.15f, 2, "RiffRiotAmpChug1", "RiffRiotAmpChug2", "RiffRiotAmpChug1", "RiffRiotAmpChug3"));
            foreach (string retired in new[] { "PowerChord", "DiveBomb", "LandingChord", "AmpChug" })
                AssetDatabase.DeleteAsset($"{VfxFolder}/RiffRiot_Sfx_{retired}.prefab"); // first pass carried them on silent particles
            pulse.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            speakerMovement.ApplySeparation = false;
            speaker.EnemyName = "Riot Amp";
            speaker.Tier = EnemyTier.Specialist;
            speaker.Stats.Movement = new AssetRef<EnemyMovementData>(speakerMovement.Guid);
            speaker.Stats.MoveSpeed = FP._0;
            speaker.Stats.Radius = F("1.1");
            speaker.Stats.HealthMultiplier = F("2");
            speaker.Stats.ShieldMultiplier = FP._0;
            speaker.Stats.UseWaypointDetour = false;
            speaker.Actions.BasicAction = new AssetRef<EnemyActionData>(pulse.Guid);
            speaker.Actions.SkillActions = new List<AssetRef<EnemyActionData>>();
            speaker.Passives = new List<AssetRef<EnemyPassiveData>>();
            speaker.ViewPrefab = speakerView;
            speaker.Faction = null;
            speaker.ExplosionColor = RiffPink;
            speaker.Description = "Riff Riot's speaker stack. Blasts rings of sound - break both to drop the punk to his knees.";

            stageDelivery.SpeakerEnemy = new AssetRef<EnemyDataAsset>(speaker.Guid);
            stageDelivery.SpawnDistanceMin = 4;
            stageDelivery.SpawnDistanceMax = 7;
            stageDelivery.MinSeparation = 7;
            stageDelivery.MaxHeightDifference = FP._1;
            stageDelivery.MinPlayerDistance = 2;
            stageDelivery.MaxDuration = 14;
            stageDelivery.FirstEmitDelay = FP._1;
            stageDelivery.EmitInterval = F("1.6");
            stageDelivery.ExposedAction = new AssetRef<EnemyActionData>(kneel.Guid);
            stageDelivery.BlockedActionsWhileActive = new List<AssetRef<EnemyActionData>> { new AssetRef<EnemyActionData>(leap.Guid) };
            stageDelivery.ClosingAction = new AssetRef<EnemyActionData>(leap.Guid);

            Action(stage, "Som no Talo", engage: 16, damageRange: 2, damage: FP._0, anticipation: F("0.9"), telegraphAt: FP._1,
                downTime: F("0.6"), cooldown: 20, delivery: stageDelivery, effects: new List<AssetRef<HitEffectData>>());
            stage.SelectionWeight = 2;
            stage.Telegraph = default;
            stage.AnticipationStep = Step(AttackAnimationType.Pulse, 0.9f, Sprite("RiffRiot_PowerChord"));
            stage.BeginStep = Step(AttackAnimationType.PunchScale, 0.6f, Sprite("RiffRiot_PowerChord"),
                Particle(waveRing, Vector3.zero, 1.6f, snap: true), shake: 0.8f,
                additional: new[] { Extra(pinkBlast, new Vector3(0f, 1f, 0f), 1.4f) });
            stage.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);
            stage.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- Kneel (stagger break + speaker exposure)
            kneelDelivery.Duration = 3;
            kneelDelivery.DamageMultiplier = F("1.5");
            kneel.Delivery = new AssetRef<EnemyDeliveryData>(kneelDelivery.Guid);
            kneel.EngageRange = FP._0; // never chosen by the AI - only ever forced
            kneel.BeginStep = Step(AttackAnimationType.PunchScale, 0.3f, Sprite("RiffRiot_Stagger"),
                Particle(crystals, Vector3.zero, 0.4f, snap: true), shake: 1f);
            kneel.OnGoingStep = Step(AttackAnimationType.None, 3f, Sprite("RiffRiot_Stagger"));
            kneel.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- Roar (phase 2 entry)
            roarDelivery.Duration = F("1.4");
            Action(roar, "Roar", engage: FP._0, damageRange: 2, damage: FP._0, anticipation: FP._0, telegraphAt: FP._1,
                downTime: F("0.4"), cooldown: FP._1, delivery: roarDelivery, effects: new List<AssetRef<HitEffectData>>());
            roar.Telegraph = default;
            roar.AnticipationStep = Step(AttackAnimationType.None, 0.1f, null);
            roar.BeginStep = Step(AttackAnimationType.Inflate, 1.4f, Sprite("RiffRiot_Roar"),
                Particle(waveRing, Vector3.zero, 2.2f, snap: true), shake: 2f,
                additional: new[] { Extra(pinkBlast, Vector3.zero, 2f, snap: true), Extra(crystals, Vector3.zero, 0.6f, snap: true) });
            roar.OnGoingStep = Step(AttackAnimationType.Shake, 1.3f, Sprite("RiffRiot_Roar"));
            roar.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- the boss
            boss.EnemyName = "Riff Riot";
            boss.Title = "RIFF RIOT";
            boss.Subtitle = "Neon Punk";
            boss.UiSprite = Sprite("RiffRiot_Idle");
            boss.Description = "Neon's loudest punk. Swings a guitar-axe, drops the stage on you and turns the speakers up to eleven.";
            boss.CatalogSubtitle = "Neon Punk";
            boss.ExplosionColor = RiffPink;
            boss.Stats.MoveSpeed = F("5");
            boss.Stats.HealthMultiplier = F("1.2");
            boss.Stats.Radius = F("1.1");
            boss.Actions.BasicAction = default;
            boss.Actions.SkillActions = new List<AssetRef<EnemyActionData>>
            {
                new AssetRef<EnemyActionData>(powerChord.Guid), // 0
                new AssetRef<EnemyActionData>(leap.Guid),       // 1
                new AssetRef<EnemyActionData>(stage.Guid),      // 2
                new AssetRef<EnemyActionData>(kneel.Guid),      // 3 - forced only (stagger / both speakers broken)
                new AssetRef<EnemyActionData>(roar.Guid),       // 4 - forced only (phase 2 entry)
            };
            boss.Passives = new List<AssetRef<EnemyPassiveData>>();
            boss.GlobalActionSlots = new List<int> { 0, 1, 2 };
            // Power Chord comes as a double strum: the second one is a full normal cast (own telegraph, re-aimed at
            // the same target), so a hero who dodged the first has to keep moving.
            boss.ComboChains = new List<BossComboChainData>
            {
                new BossComboChainData
                {
                    TriggerAction = new AssetRef<EnemyActionData>(powerChord.Guid),
                    RepeatCount = 2,
                    RetargetEachRepeat = false,
                },
            };
            boss.DeferForcedActionsWhileActive = true;
            boss.DebugPrototype = new AssetRef<EntityPrototype>(bossPrototype.Guid);

            StaggerProfileData stagger = w1Boss.Stagger;
            stagger.ThresholdPercents = w1Boss.Stagger.ThresholdPercents.ToList();
            stagger.OnBreakForcedAction = new AssetRef<EnemyActionData>(kneel.Guid);
            boss.Stagger = stagger;

            // Same kit in both phases; phase 2 only raises the cadence through the recovery beat (which also tightens the
            // speaker emission interval) plus a little move speed - windups (= reaction time) are untouched.
            boss.Phases = new List<BossPhaseData>
            {
                new BossPhaseData
                {
                    EntryTrigger = BossPhaseEntryTrigger.HealthThreshold,
                    HealthPercentThreshold = FP._1,
                    ActionPoolSlots = new List<int>(),
                },
                new BossPhaseData
                {
                    EntryTrigger = BossPhaseEntryTrigger.HealthThreshold,
                    HealthPercentThreshold = FP._0_50,
                    ActionPoolSlots = new List<int>(),
                    EntryAction = new AssetRef<EnemyActionData>(roar.Guid),
                    Modifiers = new BossStatModifiers
                    {
                        RecoveryMultiplier = F("1.6"),
                        MoveSpeedMultiplier = F("1.2"),
                    },
                },
            };

            foreach (Object asset in new Object[] { boss, speaker, speakerMovement, soundProjectile, soundMovement, kneel, leapTelegraph,
                         powerChord, powerChordDelivery, powerChordTelegraph, leap, leapDelivery, stage, stageDelivery, kneelDelivery, roar, roarDelivery,
                         pulse, pulseDelivery, pulseTelegraph })
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            ConfigureBossPrefab(material, boss);
            AssetDatabase.SaveAssets();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            Debug.Log($"[RiffRiot] Built Riff Riot - boss {boss.Guid}, prototype {bossPrototype.Guid}, speaker {speaker.Guid}");
        }

        // ------------------------------------------------------------------ sprites / material

        private static void SliceSheet()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;

#pragma warning disable CS0618 // legacy spritesheet API - same as RukkElitesSpriteSlicer
            importer.spritesheet = Sprites.Select(s => new SpriteMetaData
            {
                name = s.Name,
                rect = s.Rect,
                alignment = (int)SpriteAlignment.Custom,
                pivot = s.Pivot,
            }).ToArray();
#pragma warning restore CS0618

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            _sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        }

        private static Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out Sprite sprite) == false)
                throw new System.InvalidOperationException($"[RiffRiot] sprite {name} not found in {SheetPath}");

            return sprite;
        }

        // Own copy of the W1 boss's pink-crystal glow material (same shader), so tuning Riff Riot never touches W1.
        private static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            GameObject w1 = AssetDatabase.LoadAssetAtPath<GameObject>(W1BossPrefabPath);
            Material source = w1.GetComponentsInChildren<SpriteRenderer>(true).Select(r => r.sharedMaterial).First(m => m != null && m.name == W1BossMaterialName);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), MaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        // ------------------------------------------------------------------ guitar SFX

        // Enemies-group SoundData (Assets/_Project/Audio/Enemies/<name>) over the generated guitar clips, already trimmed/
        // faded by the generator; `clips` default to the one named like the sound. Several clips = a riff: Sequential
        // walks them in order.
        private static SoundData GuitarSound(string name, float volume, float cooldown, int maxConcurrent, params string[] clips)
        {
            if (clips.Length == 0)
                clips = new[] { name };

            AudioClip[] audio = clips.Select(c => AssetDatabase.LoadAssetAtPath<AudioClip>($"{GuitarClipFolder}/{c}.wav")).ToArray();
            SoundData sound = SoundDataCreator.CreateOrUpdate($"{SoundFolder}/{name}.asset", SoundGroup.Enemies, audio);
            sound.pick = clips.Length > 1 ? SoundData.PickMode.Sequential : SoundData.PickMode.RandomNoRepeat;
            sound.volume = new Vector2(volume, volume);
            sound.pitch = new Vector2(0.98f, 1.02f);
            sound.cooldown = cooldown;
            sound.maxConcurrent = maxConcurrent;
            return sound;
        }

        // ------------------------------------------------------------------ sound wave VFX

        // Cone: three crescent waves launched one after another along the root's forward - the aim, once the step aligns
        // the root to the enemy's direction - growing so each spans the 90 deg cone's width where it is and reaching
        // `range` as it fades. Tuned for Neon City's dark asphalt and rendered without post-processing (no Bloom), so the
        // neon glow is faked with additive layers: a wide soft pink halo (wave3, additive) behind, the pink body (wave4,
        // alpha blended - keeps the colour solid) and a thin near-white hot core on top; plus an additive glow1 flash
        // at the guitar and additive white-pink spark streaks thrown inside the cone.
        private static ParticleSystem BuildSoundCone(string name, float range)
        {
            Material body = EnsureParticleMaterial($"{VfxFolder}/{name}.mat", "Mobile/Particles/Alpha Blended", CrescentTexturePath);
            Material halo = EnsureParticleMaterial($"{VfxFolder}/{name}_Halo.mat", "Mobile/Particles/Additive", SoftCrescentTexturePath);
            Material core = EnsureParticleMaterial($"{VfxFolder}/{name}_Core.mat", "Mobile/Particles/Alpha Blended", CrescentTexturePath);
            Material flash = EnsureParticleMaterial($"{VfxFolder}/RiffRiot_GlowFlash.mat", "Mobile/Particles/Additive", GlowTexturePath);
            Material sparks = EnsureParticleMaterial($"{VfxFolder}/RiffRiot_Sparks.mat", "Mobile/Particles/Additive", StretchyTexturePath);
            const float lifetime = 0.42f;

            return SaveVfx(name, lifetime + 0.3f, root =>
            {
                AddCrescentLayer(root, "Halo", halo, range, lifetime, widthScale: 1.7f, lengthScale: 1.05f,
                    new Color(1f, 0.12f, 0.6f, 0.85f), new Color(0.9f, 0.2f, 0.85f, 0.85f), height: 0.2f, sortingFudge: 2f);
                AddCrescentLayer(root, "Waves", body, range, lifetime, widthScale: 1f, lengthScale: 1f,
                    RiffPink, new Color(1f, 0.45f, 0.8f, 1f), height: 0.25f, sortingFudge: 0f);
                AddCrescentLayer(root, "Core", core, range, lifetime, widthScale: 0.4f, lengthScale: 0.88f,
                    new Color(1f, 0.95f, 0.98f, 1f), new Color(1f, 0.88f, 0.96f, 1f), height: 0.3f, sortingFudge: -2f);

                // Bright pink-white pop where the chord leaves the guitar.
                ParticleSystem pop = AddChildSystem(root, "Flash", new Vector3(0f, 1f, 0.9f), Quaternion.identity);
                ParticleSystem.MainModule popMain = pop.main;
                popMain.duration = 0.2f;
                popMain.startLifetime = 0.22f;
                popMain.startSpeed = 0f;
                popMain.startSize = 4f;
                popMain.startColor = new Color(1f, 0.4f, 0.8f, 1f);
                popMain.maxParticles = 2;
                ParticleSystem.EmissionModule popEmission = pop.emission;
                popEmission.rateOverTime = 0f;
                popEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                ParticleSystem.ShapeModule popShape = pop.shape;
                popShape.enabled = false;
                ParticleSystem.SizeOverLifetimeModule popSize = pop.sizeOverLifetime;
                popSize.enabled = true;
                popSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.2f));
                ParticleSystem.ColorOverLifetimeModule popColor = pop.colorOverLifetime;
                popColor.enabled = true;
                popColor.color = Fade(0.2f);
                pop.GetComponent<ParticleSystemRenderer>().sharedMaterial = flash;

                AddSparks(root, range, sparks);
            });
        }

        // One crescent layer of the cone - every layer shares the same three bursts/speed/growth, so they stay stacked.
        private static void AddCrescentLayer(GameObject root, string name, Material material, float range, float lifetime,
            float widthScale, float lengthScale, Color colorA, Color colorB, float height, float sortingFudge)
        {
            const float startDistance = 0.8f;

            // Flat on the ground: quad X/Y -> world X/Z, quad "up" (Y) = forward.
            ParticleSystem waves = AddChildSystem(root, name, new Vector3(0f, height, 0f), Quaternion.Euler(90f, 0f, 0f));
            ParticleSystem.MainModule main = waves.main;
            main.duration = 0.3f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            // The crescent texture's long side is its Y; turned 90 deg so it lies across the cone, bulge leading.
            main.startRotation = Mathf.PI / 2f;
            main.startSize3D = true;
            float length = 2f * range * Mathf.Sin(45f * Mathf.Deg2Rad) * lengthScale; // cone chord at full range
            main.startSizeX = length * 0.25f * widthScale;
            main.startSizeY = length;
            main.startSizeZ = 1f;
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.maxParticles = 8;

            ParticleSystem.EmissionModule emission = waves.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.07f, 1), new ParticleSystem.Burst(0.14f, 1) });

            ParticleSystem.ShapeModule shape = waves.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.001f;
            shape.position = new Vector3(0f, startDistance, 0f);
            shape.randomDirectionAmount = 0f;

            ParticleSystem.VelocityOverLifetimeModule velocity = waves.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            // Stops a little short of `range`, so the crescent's leading bulge (not its centre) lands on the cone's edge.
            velocity.y = new ParticleSystem.MinMaxCurve((range - 0.6f - startDistance) / lifetime);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.SizeOverLifetimeModule size = waves.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, startDistance / range, 1f, 1f));

            ParticleSystem.ColorOverLifetimeModule color = waves.colorOverLifetime;
            color.enabled = true;
            color.color = Fade(0.55f);

            var renderer = waves.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = material;
            renderer.sortingFudge = sortingFudge;
        }

        // Ring: a flat pink shockwave ring (ring_ADD, the Rift Pink blast's own ring) growing to `radius`, twice.
        private static ParticleSystem BuildSoundRing(string name, float radius)
        {
            const float lifetime = 0.4f;

            return SaveVfx(name, lifetime + 0.3f, root =>
            {
                ParticleSystem rings = AddChildSystem(root, "Rings", new Vector3(0f, 0.25f, 0f), Quaternion.Euler(90f, 0f, 0f));
                ParticleSystem.MainModule main = rings.main;
                main.duration = 0.3f;
                main.startLifetime = lifetime;
                main.startSpeed = 0f;
                main.startSize = radius * 2f;
                main.startColor = RiffPink;
                main.maxParticles = 6;

                ParticleSystem.EmissionModule emission = rings.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.1f, 1) });

                ParticleSystem.ShapeModule shape = rings.shape;
                shape.enabled = false;

                ParticleSystem.SizeOverLifetimeModule size = rings.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 2.4f, 2.4f), new Keyframe(1f, 1f, 0f, 0f)));

                ParticleSystem.ColorOverLifetimeModule color = rings.colorOverLifetime;
                color.enabled = true;
                color.color = Fade(0.5f);

                var renderer = rings.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
            });
        }

        private static void AddSparks(GameObject root, float range, Material material)
        {
            ParticleSystem sparks = AddChildSystem(root, "Sparks", new Vector3(0f, 0.7f, 0.4f), Quaternion.identity);
            ParticleSystem.MainModule main = sparks.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(range * 1.8f, range * 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(RiffPink, new Color(1f, 0.95f, 0.98f, 1f));
            main.maxParticles = 24;

            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.25f;

            ParticleSystem.ColorOverLifetimeModule color = sparks.colorOverLifetime;
            color.enabled = true;
            color.color = Fade(0.4f);

            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3f;
            renderer.sharedMaterial = material;
        }

        // Root = a silent ParticleSystem (no emission/renderer) so AttackVisualStep/EffectsManager can play the whole
        // hierarchy through it; every child scales with it (Hierarchy).
        private static ParticleSystem SaveVfx(string name, float duration, System.Action<GameObject> build)
        {
            var root = new GameObject(name);

            try
            {
                ParticleSystem rootPs = root.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule rootMain = rootPs.main;
                rootMain.duration = duration;
                rootMain.loop = false;
                rootMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystem.EmissionModule rootEmission = rootPs.emission;
                rootEmission.enabled = false;
                ParticleSystem.ShapeModule rootShape = rootPs.shape;
                rootShape.enabled = false;
                root.GetComponent<ParticleSystemRenderer>().enabled = false;

                build(root);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{VfxFolder}/{name}.prefab");
                return prefab.GetComponent<ParticleSystem>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem AddChildSystem(GameObject root, string name, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = 0f;
            return ps;
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

        private static Gradient Fade(float holdUntil)
        {
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return fade;
        }

        // ------------------------------------------------------------------ prefabs

        private static GameObject BuildSpeakerView(Material material)
        {
            var go = new GameObject("RiffRiotSpeaker");

            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = Sprite("RiffRiot_Speaker");
                renderer.sharedMaterial = material;

                var rig = go.AddComponent<EnemyViewRig>();
                var shadow = go.AddComponent<HasShadow>();

                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("referenceSprite").objectReferenceValue = renderer;
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                var shadowSo = new SerializedObject(shadow);
                shadowSo.FindProperty("baseScale").floatValue = 5f;
                shadowSo.FindProperty("shadowOffset").vector2Value = new Vector2(0f, 0.2f);
                shadowSo.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(go, SpeakerViewPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // Speaker ring projectile = a neon energy orb: the enemy bullet's rim-shaded sphere with its OWN material (near-white
        // core, Riff pink rim), the existing alpha "Glow" turned into a soft pink halo, plus an additive pink neon halo
        // and a small additive white-hot centre (glow1). Children are matched by name, so re-running doesn't stack them.
        private const string OrbMaterialPath = "Assets/_Project/Prefabs/Effects/RiffRiot/RiffRiot_SoundOrb.mat";
        private const string EnemyBulletMaterialPath = "Assets/_Project/Art/Material/BulletParticleEnemy.mat";

        private static void TintProjectileView()
        {
            EnsureFolder(VfxFolder);

            var orbMaterial = AssetDatabase.LoadAssetAtPath<Material>(OrbMaterialPath);
            if (orbMaterial == null)
            {
                AssetDatabase.CopyAsset(EnemyBulletMaterialPath, OrbMaterialPath);
                orbMaterial = AssetDatabase.LoadAssetAtPath<Material>(OrbMaterialPath);
            }

            // The shader multiplies the rim colour by _RimIntensity (the enemy bullet uses 10, which clips pink to white),
            // so keep it near 1 and widen the band instead: white core, magenta-pink edge.
            orbMaterial.SetColor("_TintColor", new Color(1f, 0.96f, 0.99f, 1f));
            orbMaterial.SetColor("_RimColor", new Color(1f, 0.12f, 0.68f, 1f));
            orbMaterial.SetFloat("_RimIntensity", 1.15f);
            orbMaterial.SetFloat("_RimPower", 1.4f);
            orbMaterial.SetFloat("_RimThreshold", 0.2f);
            orbMaterial.SetFloat("_RimSoftness", 0.45f);
            EditorUtility.SetDirty(orbMaterial);

            Material neonGlow = EnsureParticleMaterial($"{VfxFolder}/RiffRiot_GlowFlash.mat", "Mobile/Particles/Additive", GlowTexturePath);

            GameObject root = PrefabUtility.LoadPrefabContents(ProjectilePrefabPath);

            try
            {
                Transform core = root.transform.Find("BulletMeshSmallFire");
                core.localScale = new Vector3(1.7f, 1.7f, 1.7f); // round orb, not a stretched bolt
                SetStartColor(core.GetComponent<ParticleSystem>(), Color.white);
                core.GetComponent<ParticleSystemRenderer>().sharedMaterial = orbMaterial;

                Transform trail = core.Find("SparkTrail");
                SetStartColor(trail.GetComponent<ParticleSystem>(), new Color(1f, 0.72f, 0.92f, 1f));

                Transform glow = core.Find("Glow");
                ParticleSystem glowPs = glow.GetComponent<ParticleSystem>();
                SetStartColor(glowPs, new Color(1f, 0.12f, 0.7f, 0.35f));
                WhitenColorOverLifetime(glowPs); // the enemy bullet's Glow fades through ORANGE, which tinted every halo coral
                ParticleSystem.MainModule glowMain = glowPs.main;
                glowMain.startSize = 1.5f;

                // Same emission/lifetime as Glow (cloned), only colour/size/material differ.
                ParticleSystem halo = EnsureClone(glow, "NeonHalo");
                SetStartColor(halo, new Color(1f, 0.15f, 0.72f, 0.8f));
                ParticleSystem.MainModule haloMain = halo.main;
                haloMain.startSize = 1.2f;
                AttachToOrb(halo);
                WhitenColorOverLifetime(halo);
                halo.GetComponent<ParticleSystemRenderer>().sharedMaterial = neonGlow;

                ParticleSystem hot = EnsureClone(glow, "HotCore");
                SetStartColor(hot, new Color(1f, 0.95f, 1f, 0.85f));
                ParticleSystem.MainModule hotMain = hot.main;
                hotMain.startSize = 0.55f;
                AttachToOrb(hot);
                WhitenColorOverLifetime(hot);
                hot.GetComponent<ParticleSystemRenderer>().sharedMaterial = neonGlow;

                PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Halo layers sit ON the orb (local space, steady emission) instead of trailing behind it like the cloned Glow.
        private static void AttachToOrb(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.12f;
            main.startSpeed = 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 30f;
            emission.rateOverDistance = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
        }

        // Keeps the alpha fade, drops the hue - so start colour alone decides the colour.
        private static void WhitenColorOverLifetime(ParticleSystem ps)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            if (colorOverLifetime.enabled == false || colorOverLifetime.color.gradient == null)
                return;

            Gradient source = colorOverLifetime.color.gradient;
            var white = new Gradient();
            white.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, source.alphaKeys);
            colorOverLifetime.color = white;
        }

        private static void SetStartColor(ParticleSystem ps, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = color;
        }

        private static ParticleSystem EnsureClone(Transform source, string name)
        {
            Transform existing = source.parent.Find(name);
            if (existing != null)
                return existing.GetComponent<ParticleSystem>();

            GameObject clone = Object.Instantiate(source.gameObject, source.parent);
            clone.name = name;
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            return clone.GetComponent<ParticleSystem>();
        }

        private static void ConfigureBossPrefab(Material material, BossDataAsset boss)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);

            try
            {
                EnemyViewRig rig = root.GetComponentInChildren<EnemyViewRig>(true);
                rig.gameObject.name = "RiffRiot-Body";

                var renderer = rig.GetComponent<SpriteRenderer>();
                renderer.sprite = Sprite("RiffRiot_Idle");
                renderer.sharedMaterial = material;

                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("dyingSprite").objectReferenceValue = Sprite("RiffRiot_Stagger");
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                // W1-only smoke plume above the head.
                Transform smoke = rig.transform.Find("VFX3D_Smoke");
                if (smoke != null)
                    Object.DestroyImmediate(smoke.gameObject);

                var enemyProto = root.GetComponent<QPrototypeEnemy>();
                var so = new SerializedObject(enemyProto);
                SerializedProperty data = so.FindProperty("Prototype.EnemyData.Id.Value");
                if (data == null)
                    throw new System.InvalidOperationException("[RiffRiot] QPrototypeEnemy.Prototype.EnemyData not found - serialized layout changed");

                data.longValue = (long)boss.Guid.Value;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static EntityPrototype LoadPrototype(string prefabPath)
        {
            string protoPath = prefabPath.Replace(".prefab", "EntityPrototype.qprototype");
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            EntityPrototype prototype = AssetDatabase.LoadAllAssetsAtPath(protoPath).OfType<EntityPrototype>().FirstOrDefault();

            if (prototype == null || prototype.Guid.IsValid == false)
                throw new System.InvalidOperationException($"[RiffRiot] no EntityPrototype generated for {prefabPath} (expected {protoPath})");

            return prototype;
        }

        // ------------------------------------------------------------------ asset helpers

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

        // Main-asset template -> standalone copy. Identifier reset so the importer stamps a fresh guid.
        private static T CloneOrLoad<T>(string templatePath, string name) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(PathOf(name));
            if (existing != null)
                return existing;

            T template = AssetDatabase.LoadAllAssetsAtPath(templatePath).OfType<T>().First();
            return SaveClone(template, name);
        }

        // Nested (sub-asset) template -> standalone copy.
        private static T CloneSubOrLoad<T>(string templatePath, string name, System.Func<T, bool> filter = null) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(PathOf(name));
            if (existing != null)
                return existing;

            T template = AssetDatabase.LoadAllAssetsAtPath(templatePath).OfType<T>().First(t => filter == null || filter(t));
            return SaveClone(template, name);
        }

        private static T SaveClone<T>(T template, string name) where T : AssetObject
        {
            T clone = Object.Instantiate(template);
            clone.name = name;
            clone.Identifier = default;
            AssetDatabase.CreateAsset(clone, PathOf(name));
            return clone;
        }

        private static void EnsureCopy(string from, string to)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(to) != null)
                return;

            if (AssetDatabase.CopyAsset(from, to) == false)
                throw new System.InvalidOperationException($"[RiffRiot] could not copy {from} -> {to}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static ParticleSystem LoadParticle(string path)
        {
            var particle = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
            if (particle == null)
                Debug.LogWarning($"[RiffRiot] particle prefab missing at {path} - step left without it");

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

        private static AttackVisualStep Step(AttackAnimationType animation, float duration, Sprite sprite, AttackVisualParticle particle = null,
            float shake = 0f, AttackVisualParticle[] additional = null)
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

        private static AttackVisualParticle Particle(ParticleSystem prefab, Vector3 offset, float scale, bool snap = false, bool parented = false,
            ParticleAnchor anchor = ParticleAnchor.OnSelf)
        {
            return Extra(prefab, offset, scale, snap, parented, anchor);
        }

        // Rotated to the boss's aim (same muzzle-flash setup as the W1 boss cone punch) - the directional read of a cone.
        private static AttackVisualParticle Directional(ParticleSystem prefab, Vector3 offset, float scale)
        {
            AttackVisualParticle particle = Extra(prefab, offset, scale);
            particle.AlignToEnemyDirection = true;
            particle.RotationOffset = new Vector3(0f, 270f, -15f);
            return particle;
        }

        private static AttackVisualParticle Extra(ParticleSystem prefab, Vector3 offset, float scale, bool snap = false, bool parented = false,
            ParticleAnchor anchor = ParticleAnchor.OnSelf)
        {
            return new AttackVisualParticle
            {
                ParticlePrefab = prefab,
                Anchor = anchor,
                Offset = offset,
                SnapToGround = snap,
                Parented = parented,
                Scale = scale,
            };
        }
    }
}
