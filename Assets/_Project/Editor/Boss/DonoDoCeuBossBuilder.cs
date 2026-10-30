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
    /// Builds the Favela boss Dono do Céu (docs/dono-do-ceu-boss.md): sliced sprites, entity/view/projectile/hazard
    /// prefabs, the whirlwind variant and every Quantum asset. Idempotent - re-running keeps every file (so every Unity and
    /// Quantum guid) and re-applies the tuning below, so THIS file is the single source of truth for the numbers; hand
    /// edits to the generated assets are overwritten. Templates (W1 boss prefab/asset, W1 boss cone telegraph, W1 Gunner
    /// projectile, basic enemy projectile prefab, Epic Toon FX whirlwind/feather/ring, W1 leap effects) are only read or
    /// copied, never modified. Nothing under W1/W2/Neon (Riff Riot) or Yoru is touched.
    /// </summary>
    public static class DonoDoCeuBossBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Sprites/Enemy/Favela/DonoDoCeu";
        private const string SheetAPath = ArtFolder + "/DonoDoCeu_SheetA.png";
        private const string SheetBPath = ArtFolder + "/DonoDoCeu_SheetB.png";
        private const string TrashSheetPath = ArtFolder + "/DonoDoCeu_Trash.png";
        private const string BodyMaterialPath = ArtFolder + "/DonoDoCeu_PinkCrystalGlow.mat";

        private const string Root = "Assets/_QuantumUser/Resources/Enemy/Favela/Boss/DonoDoCeu";
        private const string BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/Favela-DonoDoCeuPrefab.prefab";
        private const string TornadoEntityPath = "Assets/_QuantumUser/Entities/Enemies/Favela-DonoDoCeuTornado.prefab";
        private const string ProjectileFolder = "Assets/_QuantumUser/Entities/Projectile/DonoDoCeu";
        private const string VfxFolder = "Assets/_Project/Prefabs/Effects/DonoDoCeu";
        private const string DirectorResources = "Assets/_Project/Resources/DonoDoCeu"; // BossHazardFxDirector loads from here

        // Templates (read/copied only).
        private const string W1BossPrefabPath = "Assets/_QuantumUser/Entities/Enemies/World1BossPrefab.prefab";
        private const string W1BossPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss.asset";
        private const string W1ConeTelegraphPath = "Assets/_QuantumUser/Resources/Enemy/World1Boss/W1-World1Boss-ConePunchTelegraph.asset";
        private const string W1GunnerPath = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies/W1-Gunner.asset";
        private const string GenericDirectHitPath = "Assets/_QuantumUser/Resources/Weapons/Projectile/GenericDirectHitData 1.asset";
        private const string EnemyProjectilePrefabPath = "Assets/_QuantumUser/Entities/Projectile/BasicEnemyWeaponProjectileEntity.prefab";
        private const string Cone90TelegraphPrefabPath = "Assets/_QuantumUser/Resources/QuantumViewAssets/Telegraph/ConeTelegraph90.prefab";
        private const string WhirlwindPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Environment/Weather/Whirlwinds/Whirlwind2.prefab"; // a real funnel column - reads as a tornado top-down
        private const string FeatherTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/feather.png";
        private const string RingTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/ring.png";
        private const string GlowTexturePath = "Assets/3rd-party/Epic Toon FX/Textures/glow1.png";
        private const string BodySlamPath = "Assets/_Project/Prefabs/Effects/RR_ToonBodySlam.prefab";
        private const string ToonBodySlamPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Brawling/Soft/ToonBodySlam.prefab";
        private const string FeatherExplosionPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Interactive/Feathers/FeatherExplosion.prefab";
        private const string MusicalNoteExplosionPath = "Assets/3rd-party/Epic Toon FX/Prefabs/Misc/MusicalNoteExplosion.prefab";
        private const string MagicChargePath = "Assets/3rd-party/Epic Toon FX/Prefabs/Combat/Magic/Charge/MagicChargeBlue.prefab";
        private const string WhistleClipPath = "Assets/_Project/Audio/Library/RefereeWhistleBlow.wav"; // freesound 218318 (splicesound)
        private const string WhistleSoundPath = "Assets/_Project/Audio/Enemies/DonoDoCeuWhistle.asset";
        // Crowd booing (freesound 264378, howardv): 4.5s cut from 0.4s of the take (= CrowdMark.MarkDuration), 0.18s swell-in,
        // 1.6s fade-out, rumble high-passed, levelled ~6dB under the whistle - the edit is baked into the wav.
        private const string CrowdBooClipPath = "Assets/_Project/Audio/Library/DonoDoCeuCrowdBoo.wav";
        private const string CrowdBooSoundPath = "Assets/_Project/Audio/Enemies/DonoDoCeuCrowdBoo.asset";
        private const string CrystalSparksPath = "Assets/_Project/Resources/BossDefeat/BossDefeatCrystalSparks.prefab";
        private const string JumpPuffGuid = "dbe2ea8610622df44a4a79ba99af5ddc";  // VFX3D_SimpleJump (W1 boss leap take-off)
        private const string SmokeGuid = "8aef01eeca7c56b48b7d99650ec01d4a";     // VFX3D_Smoke
        private const string W1BossMaterialName = "SpriteSelectiveColorGlow_PinkCrystal";

        // Derived from Assets/0_Refs/FavelaBoss/DonoDoCeu-v3-SheetA/B.png and CrowdTrashSpriteSheet-v1.png (each pose
        // isolated by its own alpha component - the sheets' cells overlap - glow halo removed, nothing redrawn). Pivot:
        // grounded poses on their talon line; airborne poses (HoverUp/Down, SwoopPrep/Dive, StaggerHit, Falling) on the
        // body (head + the idle's head-to-feet offset), so flight height comes only from the simulation.
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
            new SpriteSpec("DonoDoCeu_Idle", "A", new Rect(117f, 815f, 405f, 290f), new Vector2(0.5332f, 0.0207f)),
            new SpriteSpec("DonoDoCeu_TakeoffPrep", "A", new Rect(756f, 838f, 408f, 245f), new Vector2(0.7024f, 0.0245f)),
            new SpriteSpec("DonoDoCeu_HoverUp", "A", new Rect(1340f, 791f, 520f, 338f), new Vector2(0.4690f, -0.0236f)),
            new SpriteSpec("DonoDoCeu_HoverDown", "A", new Rect(26f, 443f, 587f, 266f), new Vector2(0.4770f, -0.0890f)),
            new SpriteSpec("DonoDoCeu_WhistlePrep", "A", new Rect(791f, 420f, 338f, 313f), new Vector2(0.5372f, 0.0192f)),
            new SpriteSpec("DonoDoCeu_WhistleBlow", "A", new Rect(1390f, 437f, 419f, 279f), new Vector2(0.5510f, 0.0215f)),
            new SpriteSpec("DonoDoCeu_TornadoPrep", "A", new Rect(72f, 6f, 495f, 372f), new Vector2(0.4693f, 0.0161f)),
            new SpriteSpec("DonoDoCeu_TornadoRelease", "A", new Rect(752f, 51f, 416f, 283f), new Vector2(0.4179f, 0.0212f)),
            new SpriteSpec("DonoDoCeu_TornadoRecover", "A", new Rect(1373f, 61f, 454f, 262f), new Vector2(0.5620f, 0.0229f)),
            new SpriteSpec("DonoDoCeu_FeatherPrep", "B", new Rect(47f, 782f, 546f, 357f), new Vector2(0.5394f, 0.0168f)),
            new SpriteSpec("DonoDoCeu_FeatherRelease", "B", new Rect(645f, 814f, 630f, 292f), new Vector2(0.5453f, 0.0205f)),
            new SpriteSpec("DonoDoCeu_SwoopPrep", "B", new Rect(1387f, 800f, 425f, 320f), new Vector2(0.5068f, -0.1028f)),
            new SpriteSpec("DonoDoCeu_SwoopDive", "B", new Rect(84f, 419f, 472f, 314f), new Vector2(0.6380f, -0.1437f)),
            new SpriteSpec("DonoDoCeu_LandingRecover", "B", new Rect(651f, 466f, 618f, 220f), new Vector2(0.4700f, 0.0273f)),
            new SpriteSpec("DonoDoCeu_StaggerHit", "B", new Rect(1412f, 403f, 375f, 346f), new Vector2(0.3382f, 0.0852f)),
            new SpriteSpec("DonoDoCeu_Falling", "B", new Rect(94f, 21f, 451f, 343f), new Vector2(0.5964f, 0.0207f)),
            new SpriteSpec("DonoDoCeu_KneelHold", "B", new Rect(720f, 57f, 479f, 271f), new Vector2(0.4710f, 0.0221f)),
            new SpriteSpec("DonoDoCeu_KneelRecover", "B", new Rect(1365f, 38f, 470f, 308f), new Vector2(0.5542f, 0.0195f)),
            new SpriteSpec("DonoDoCeu_TrashCan", "T", new Rect(15f, 313f, 257f, 239f), new Vector2(0.5f, 0.5f)),
            new SpriteSpec("DonoDoCeu_TrashCup", "T", new Rect(316f, 310f, 231f, 245f), new Vector2(0.5f, 0.5f)),
            new SpriteSpec("DonoDoCeu_TrashPaper", "T", new Rect(29f, 32f, 230f, 224f), new Vector2(0.5f, 0.5f)),
            new SpriteSpec("DonoDoCeu_TrashBottle", "T", new Rect(295f, 19f, 274f, 250f), new Vector2(0.5f, 0.5f)),
        };

        private static readonly string[] TrashNames = { "Can", "Cup", "Paper", "Bottle" };

        private static readonly Color VultureRed = new Color(0.86f, 0.1f, 0.16f, 1f);
        private static readonly Color RiftPink = new Color(0.99f, 0.22f, 0.45f, 1f); // #FD3971

        private const float FeatherRange = 12f;
        internal const float WhistleRange = 10f;      // the whistle cone's reach (= its DamageRange and shriek FX)

        // Tornado and swoop share one high weight so they rotate on their cooldowns (the repetition penalty breaks ties);
        // the whistle sits one above it - on equal weight it lost every tie (RangeScore favours their longer EngageRange)
        // and almost never played. It wins whenever it's ready and unblocked; the others still get their turns because the
        // mechanics gate each other (barrage blocks the tornado, tornadoes block the whistle) and its cooldown is long.
        // The short-cooldown feather volley, at 1, only fills the gaps.
        private const int BigMechanicWeight = 4;

        private static Dictionary<string, Sprite> _sprites;

        [MenuItem("Tools/RiftRaiders/Boss/Build Dono do Céu (Favela)")]
        public static void Build()
        {
            EnsureFolder(Root);
            EnsureFolder(ProjectileFolder);
            EnsureFolder(VfxFolder);
            EnsureFolder(DirectorResources);

            SliceSheets();
            Material bodyMaterial = EnsureBodyMaterial();

            // ---------------------------------------------------------------- view/entity prefabs
            Material featherMaterial = EnsureParticleMaterial($"{VfxFolder}/DonoDoCeu_Feather.mat", "Mobile/Particles/Alpha Blended", FeatherTexturePath);
            Material ringMaterial = EnsureParticleMaterial($"{VfxFolder}/DonoDoCeu_Ring.mat", "Mobile/Particles/Alpha Blended", RingTexturePath);
            Material glowMaterial = EnsureParticleMaterial($"{VfxFolder}/DonoDoCeu_Glow.mat", "Mobile/Particles/Additive", GlowTexturePath);

            GameObject whirlwind = BuildWhirlwindVariant(featherMaterial, ringMaterial);
            BuildTornadoEntity(whirlwind);
            BuildDirectorFx(ringMaterial, glowMaterial);
            BuildCrowdMarkHead(ringMaterial);
            ParticleSystem trashImpact = BuildTrashImpact();
            ParticleSystem featherBurst = BuildFeatherBurst();
            ParticleSystem whistleBlast = BuildWhistleBlast();
            ParticleSystem whistleCharge = BuildWhistleCharge();

            EnsureCopy(W1BossPrefabPath, BossPrefabPath);
            for (int i = 0; i < TrashNames.Length; i++)
            {
                string path = TrashPrefabPath(i);
                EnsureCopy(EnemyProjectilePrefabPath, path);
                ConfigureTrashPrefab(path, Sprite("DonoDoCeu_Trash" + TrashNames[i]));
            }

            EnsureCopy(EnemyProjectilePrefabPath, FeatherPrefabPath);
            ConfigureFeatherPrefab(FeatherPrefabPath, featherMaterial);

            // ---------------------------------------------------------------- pass 1: every Quantum asset on disk first
            var boss = CloneOrLoad<BossDataAsset>(W1BossPath, "DonoDoCeu");
            var whistleTelegraph = CloneOrLoad<TelegraphData>(W1ConeTelegraphPath, "DonoDoCeu-WhistleTelegraph");
            var featherTelegraph = CloneOrLoad<TelegraphData>(W1ConeTelegraphPath, "DonoDoCeu-FeatherTelegraph");
            var featherProjectile = CloneSubOrLoad<ProjectileDataAsset>(W1GunnerPath, "DonoDoCeu-FeatherProjectile");
            var featherMovement = CloneSubOrLoad<StraightProjectileMovementData>(W1GunnerPath, "DonoDoCeu-FeatherMovement");

            var trashMovement = CreateOrLoad<BallisticProjectileMovementData>("DonoDoCeu-TrashMovement");
            var trashHit = CreateOrLoad<AreaHitData>("DonoDoCeu-TrashHit");
            var trashProjectiles = TrashNames.Select(n => CreateOrLoad<ProjectileDataAsset>("DonoDoCeu-Trash" + n)).ToArray();

            var slow = CreateOrLoad<StickySlowEffectData>("DonoDoCeu-WhistleSlow");
            var mark = CreateOrLoad<CrowdMarkEffectData>("DonoDoCeu-CrowdMark");

            var feather = CreateOrLoad<EnemyActionData>("DonoDoCeu-PenasDeRift");
            var featherDelivery = CreateOrLoad<FanProjectileDeliveryData>("DonoDoCeu-PenasDeRiftDelivery");
            var whistle = CreateOrLoad<EnemyActionData>("DonoDoCeu-AGeralNaoPerdoa");
            var whistleDelivery = CreateOrLoad<CrowdWhistleDeliveryData>("DonoDoCeu-AGeralNaoPerdoaDelivery");
            var tornado = CreateOrLoad<EnemyActionData>("DonoDoCeu-VendavalDaGeral");
            var tornadoDelivery = CreateOrLoad<TornadoCastDeliveryData>("DonoDoCeu-VendavalDaGeralDelivery");
            var swoop = CreateOrLoad<EnemyActionData>("DonoDoCeu-RasanteDoCapitao");
            var swoopDelivery = CreateOrLoad<SwoopDeliveryData>("DonoDoCeu-RasanteDoCapitaoDelivery");
            var kneel = CreateOrLoad<EnemyActionData>("DonoDoCeu-Kneel");
            var kneelDelivery = CreateOrLoad<AirborneKneelDeliveryData>("DonoDoCeu-KneelDelivery");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            EntityPrototype bossPrototype = LoadPrototype(BossPrefabPath);
            EntityPrototype tornadoPrototype = LoadPrototype(TornadoEntityPath);
            EntityPrototype featherPrototype = LoadPrototype(FeatherPrefabPath);
            EntityPrototype[] trashPrototypes = Enumerable.Range(0, TrashNames.Length).Select(i => LoadPrototype(TrashPrefabPath(i))).ToArray();

            // W1 boss leap's hit effects (generic damage + strong knockback) - shared effect library, only referenced.
            EnemyActionData w1Leap = AssetDatabase.LoadAllAssetsAtPath(W1BossPath).OfType<EnemyActionData>().First(a => a.name == "World1Boss-LeapAttack");
            List<AssetRef<HitEffectData>> hitAndKnock = w1Leap.Effects.ToList();
            List<AssetRef<HitEffectData>> damageOnly = hitAndKnock.Where(e => QuantumUnityDB.GetGlobalAssetEditorInstance(e) is DamageEffectData).ToList();

            ParticleSystem bodySlam = LoadParticle(BodySlamPath);
            ParticleSystem crystals = LoadParticle(CrystalSparksPath);
            ParticleSystem jumpPuff = LoadParticle(AssetDatabase.GUIDToAssetPath(JumpPuffGuid));
            GameObject cone90 = AssetDatabase.LoadAssetAtPath<GameObject>(Cone90TelegraphPrefabPath);

            // ---------------------------------------------------------------- 4. Penas de Rift (feather fan, basic pressure)
            featherMovement.Speed = FP._1;
            featherProjectile.Prototype = new AssetRef<EntityPrototype>(featherPrototype.Guid);
            featherProjectile.Movement = new AssetRef<ProjectileMovementData>(featherMovement.Guid);
            featherProjectile.Hit = new AssetRef<ProjectileHitData>(AssetDatabase.LoadAssetAtPath<ProjectileHitData>(GenericDirectHitPath).Guid);
            featherProjectile.Lifetime = 3;
            featherProjectile.MaxDistance = 6;
            featherProjectile.HitRadius = F("0.22");

            featherDelivery.ProjectileData = new AssetRef<ProjectileDataAsset>(featherProjectile.Guid);
            featherDelivery.ProjectileSpeedMultiplier = 14;
            featherDelivery.ProjectileMaxDistanceMultiplier = 4; // 24u - flies on to the edge of the screen; the telegraph stays the 12u danger cone
            featherDelivery.LeadFactor = FP._0;
            featherDelivery.SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
            featherDelivery.SpawnOffset = new FPVector3(0, 0, FP._0_50);
            featherDelivery.PelletCount = FeatherLaneCount;
            featherDelivery.SpreadAngle = 90; // = the lanes telegraph's fan; 15 deg lanes in both phases
            featherDelivery.Radial = false;
            featherDelivery.UseArc = false;

            ConeTelegraph(featherTelegraph, BuildFeatherLanesTelegraph(), FeatherRange / 6f, 0.7f);

            Action(feather, "Penas de Rift", engage: 12, damageRange: 6, damage: 11, anticipation: F("0.45"), telegraphAt: F("0.3"),
                downTime: F("0.35"), cooldown: F("1.6"), delivery: featherDelivery, effects: damageOnly);
            feather.SelectionWeight = 1; // filler: picked whenever the big three are cooling down or gated (BigMechanicWeight)
            feather.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            feather.AimLock = EnemyAimLockTiming.LocksAtPercent;
            feather.AimLockPercent = F("0.7");
            feather.Origin = EnemyActionOrigin.Self;
            feather.Telegraph = new AssetRef<TelegraphData>(featherTelegraph.Guid);
            feather.AnticipationStep = Step(AttackAnimationType.SwingBack, 0.7f, Sprite("DonoDoCeu_FeatherPrep"));
            feather.BeginStep = Step(AttackAnimationType.PunchScale, 0.7f, Sprite("DonoDoCeu_FeatherRelease"),
                Directional(featherBurst, new Vector3(0f, 0.6f, 1.2f), 0.9f), shake: 0.4f);
            feather.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);
            feather.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- 1. A Geral Não Perdoa (whistle -> crowd trash)
            slow.Duration = 2;
            slow.SpeedMultiplier = F("0.7");      // 30% slower, non-elemental (never Ice/Freeze), refresh-not-stack
            slow.OnlyWhenGrounded = false;

            trashMovement.LaunchAngle = 55;
            trashMovement.Gravity = 18;

            trashHit.BlastRadius = F("1.6");
            trashHit.TargetMask = DamageTargetMask.Players;
            trashHit.MaxHeightDifference = F("1.2");
            trashHit.TriggersSpawnUpgrades = false;
            trashHit.DetonateOnEnemyHit = false;
            trashHit.DetonateOnLevelGeometry = false; // arcs in over the arena walls and lajes...
            trashHit.PassThroughScenery = true;        // ...without settling against them; detonates on expiry = its landing (BossCrowdBarrageSystem)
            trashHit.Effects = damageOnly.ToList();
            trashHit.BlastEffectPrefab = trashImpact;

            for (int i = 0; i < trashProjectiles.Length; i++)
            {
                ProjectileDataAsset trash = trashProjectiles[i];
                trash.Prototype = new AssetRef<EntityPrototype>(trashPrototypes[i].Guid);
                trash.Movement = new AssetRef<ProjectileMovementData>(trashMovement.Guid);
                trash.Hit = new AssetRef<ProjectileHitData>(trashHit.Guid);
                trash.Lifetime = 5;
                trash.MaxDistance = FP._0;
                trash.HitRadius = F("0.4");
            }

            mark.MarkDuration = F("4.5");
            mark.Salvos = 4;
            mark.ThrowsPerSalvo = 3;
            mark.MaxTotalThrows = 12;
            mark.FirstSalvoDelay = F("0.35");
            mark.SalvoInterval = F("0.75");
            mark.ThrowInterval = F("0.15");
            mark.ThrowDamage = 13;
            mark.LandingSpreadMin = F("0.3");   // tight around the (led) target spot - every piece is a real threat
            mark.LandingSpreadMax = F("1.0");
            mark.LeadFactor = F("0.6");         // partial: walking straight still gets caught, a turn still dodges
            mark.MaxLeadDistance = F("2.5");
            mark.OutsideMargin = 3;
            mark.ThrowHeight = 5;
            mark.MaxThrowDistance = 20;
            mark.FallbackDistance = 12;
            mark.TrashProjectiles = trashProjectiles.Select(t => new AssetRef<ProjectileDataAsset>(t.Guid)).ToList();

            whistleDelivery.ConeShaped = true;
            whistleDelivery.ConeAngleDegrees = 90; // matches ConeTelegraph90
            whistleDelivery.SelfDestructs = false;
            whistleDelivery.MaxHeightDifference = F("1.2");
            whistleDelivery.BlockWhileMajorMechanicActive = true;

            ConeTelegraph(whistleTelegraph, cone90, 1f, 1f); // the standard solid red cone - the feathers get lanes

            // No damage effect on purpose: only the slow + mark.
            Action(whistle, "A Geral Não Perdoa", engage: FP.FromFloat_UNSAFE(WhistleRange), damageRange: FP.FromFloat_UNSAFE(WhistleRange), damage: FP._0, anticipation: F("0.65"), telegraphAt: F("0.15"),
                downTime: F("0.45"), cooldown: 12, delivery: whistleDelivery, effects: new List<AssetRef<HitEffectData>>
                {
                    new AssetRef<HitEffectData>(slow.Guid),
                    new AssetRef<HitEffectData>(mark.Guid),
                });
            whistle.Origin = EnemyActionOrigin.Self;
            whistle.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            whistle.AimLock = EnemyAimLockTiming.LocksAtPercent;
            whistle.AimLockPercent = F("0.65");
            whistle.SelectionWeight = BigMechanicWeight + 1; // see BigMechanicWeight
            whistle.Telegraph = new AssetRef<TelegraphData>(whistleTelegraph.Guid);
            // Charge-up at the whistle while he fills his lungs: parented, so it follows the inflate and is cut (graceful
            // stop) the moment the blow starts or the windup is interrupted.
            AttackVisualParticle charge = Particle(whistleCharge, WhistleMouthOnSprite, WhistleChargeScale);
            charge.Parented = true;
            charge.ParentToBody = true;   // Offset = the drawn beak, flipping/squashing with the sprite
            whistle.AnticipationStep = Step(AttackAnimationType.Inflate, 1f, Sprite("DonoDoCeu_WhistlePrep"), additional: new[] { charge });
            // The shriek rides the exact cone (apex at his feet, turned to the aim); the note burst at the beak carries the
            // referee whistle sound.
            AttackVisualParticle shriek = Particle(DonoDoCeuWhistleFxBuilder.Build(WhistleRange), Vector3.zero, 1f, snap: true);
            shriek.AlignToEnemyDirection = true;
            whistle.BeginStep = Step(AttackAnimationType.PunchScale, 0.9f, Sprite("DonoDoCeu_WhistleBlow"), shriek, shake: 0.6f,
                additional: new[] { Directional(whistleBlast, new Vector3(0f, 1.1f, 1f), 0.8f) });
            whistle.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);
            whistle.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- 2. Vendaval da Geral (two moving tornadoes)
            tornadoDelivery.TornadoPrototype = new AssetRef<EntityPrototype>(tornadoPrototype.Guid);
            tornadoDelivery.Count = 2;
            tornadoDelivery.MaxActive = 2;
            tornadoDelivery.SpawnInterval = F("0.35");
            tornadoDelivery.SpawnDistance = F("2.5");
            tornadoDelivery.SideAngle = 35;
            tornadoDelivery.Speed = F("4.0");
            tornadoDelivery.TurnRate = 38;
            tornadoDelivery.MaxStepHeight = F("0.6");
            tornadoDelivery.Lifetime = 7;
            tornadoDelivery.CompressTell = F("0.7");
            tornadoDelivery.Radius = F("1.6");
            tornadoDelivery.DamageInterval = F("0.5");
            tornadoDelivery.MaxHeightDifference = F("1.2");
            tornadoDelivery.PullRadius = F("3.4");
            tornadoDelivery.PullSpeed = F("1.6");
            tornadoDelivery.PulseRadius = F("2.6");
            tornadoDelivery.PulseDamage = 20;
            tornadoDelivery.Effects = damageOnly.ToList();
            tornadoDelivery.BlockWhileMajorMechanicActive = true;

            Action(tornado, "Vendaval da Geral", engage: 14, damageRange: 2, damage: 10, anticipation: F("0.6"), telegraphAt: FP._1,
                downTime: F("0.5"), cooldown: 10, delivery: tornadoDelivery, effects: damageOnly);
            tornado.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            tornado.SelectionWeight = BigMechanicWeight;
            tornado.Telegraph = default;
            tornado.AnticipationStep = Step(AttackAnimationType.Crouch, 0.9f, Sprite("DonoDoCeu_TornadoPrep"));
            // Release held through both spawns, then the exhausted recovery pose for the DownTime.
            tornado.BeginStep = Step(AttackAnimationType.PunchScale, 1.9f, null, Particle(jumpPuff, Vector3.zero, 3f, snap: true), shake: 1f);
            tornado.BeginStep.BodySpriteFrames = new[] { Sprite("DonoDoCeu_TornadoRelease"), Sprite("DonoDoCeu_TornadoRelease"), Sprite("DonoDoCeu_TornadoRecover"), Sprite("DonoDoCeu_TornadoRecover") };
            tornado.OnGoingStep = Step(AttackAnimationType.None, 0.3f, null);
            tornado.EndStep = Step(AttackAnimationType.None, 0.3f, null);

            // ---------------------------------------------------------------- 3. Rasante do Capitão (swoop)
            swoopDelivery.RiseTime = F("0.3");
            swoopDelivery.HoverAltitude = F("3.5");
            swoopDelivery.HoverTime = F("0.7");
            swoopDelivery.HoverSpeed = 10;
            swoopDelivery.HoverStandOff = 5;
            swoopDelivery.AimTime = F("0.5");
            swoopDelivery.DiveSpeed = 22;
            swoopDelivery.DiveLength = 12;
            swoopDelivery.MinOvershoot = 3;
            swoopDelivery.HitHeight = F("2.2");
            swoopDelivery.MaxHeightDifference = F("1.2");
            swoopDelivery.MaxLandingHeightDifference = F("1.5");
            swoopDelivery.BlockWhileMajorMechanicActive = true;
            swoopDelivery.ShowGroundLaneWarning = false; // ONE telegraph: the 3D flight path (SwoopTrajectoryTelegraphDirector)
            swoopDelivery.StageSteps = new[]
            {
                FlapStep(0.5f, Particle(jumpPuff, Vector3.zero, 3.5f, snap: true)),   // Rise
                FlapStep(1.4f, null),                                                   // Hover
                Step(AttackAnimationType.None, 0.75f, Sprite("DonoDoCeu_SwoopPrep")),   // Aim (lane warning on the floor)
                Step(AttackAnimationType.None, 1f, Sprite("DonoDoCeu_SwoopDive")),      // Dive
            };

            // DamageRange = half the lane width.
            Action(swoop, "Rasante do Capitão", engage: 16, damageRange: F("1.4"), damage: 30, anticipation: F("0.35"), telegraphAt: FP._1,
                downTime: F("1.0"), cooldown: F("6.5"), delivery: swoopDelivery, effects: hitAndKnock);
            swoop.DirectionTracking = DirectionUpdateMode.UpdateTargetDirectionOnAnticipation;
            swoop.SelectionWeight = BigMechanicWeight;
            swoop.Telegraph = default;
            swoop.AnticipationStep = Step(AttackAnimationType.Crouch, 0.5f, Sprite("DonoDoCeu_TakeoffPrep"));
            swoop.BeginStep = Step(AttackAnimationType.None, 0.1f, null);
            swoop.OnGoingStep = Step(AttackAnimationType.None, 0.1f, null);
            // Landing: braking pose for the whole grounded punish window.
            swoop.EndStep = Step(AttackAnimationType.PunchScale, 1.8f, Sprite("DonoDoCeu_LandingRecover"),
                Particle(bodySlam, new Vector3(0f, 0.1f, 0f), 3f, snap: true), shake: 2f);

            // ---------------------------------------------------------------- Kneel (stagger break, forced only)
            kneelDelivery.HitTime = F("0.3");
            kneelDelivery.FallSpeed = 9;
            kneelDelivery.MinFallTime = F("0.35");
            kneelDelivery.Duration = 3;
            kneelDelivery.DamageMultiplier = F("1.5");
            kneelDelivery.RiseTime = F("0.6");
            kneelDelivery.AirborneThreshold = F("0.6");
            kneelDelivery.CancelOwnedHazards = true;
            kneelDelivery.StageSteps = new[]
            {
                Step(AttackAnimationType.Shake, 0.3f, Sprite("DonoDoCeu_StaggerHit"), Particle(crystals, Vector3.zero, 0.35f), shake: 1f), // Hit
                Step(AttackAnimationType.None, 1.2f, Sprite("DonoDoCeu_Falling")),                                                       // Fall
                Step(AttackAnimationType.None, 3f, Sprite("DonoDoCeu_KneelHold"), Particle(bodySlam, Vector3.zero, 2.5f, snap: true), shake: 1.5f), // Kneel (touchdown)
                Step(AttackAnimationType.None, 0.6f, Sprite("DonoDoCeu_KneelRecover")),                                                  // Rise
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
            boss.EnemyName = "Dono do Céu";
            boss.Title = "DONO DO CÉU";
            boss.Subtitle = "O Juiz da Laje";
            boss.UiSprite = Sprite("DonoDoCeu_Idle");
            boss.Description = "The Favela's referee vulture. Blows the whistle, sets the crowd on you, whips up tornadoes and dives from the sky.";
            boss.CatalogSubtitle = "O Juiz da Laje";
            boss.ExplosionColor = VultureRed;
            boss.Stats.MoveSpeed = F("6.5");
            boss.Stats.Radius = F("1.3");
            boss.Stats.HealthMultiplier = F("1.2");
            boss.Actions.BasicAction = default;
            boss.Actions.SkillActions = new List<AssetRef<EnemyActionData>>
            {
                new AssetRef<EnemyActionData>(feather.Guid), // 0 - slot 1
                new AssetRef<EnemyActionData>(whistle.Guid), // 1 - slot 2
                new AssetRef<EnemyActionData>(tornado.Guid), // 2 - slot 3
                new AssetRef<EnemyActionData>(swoop.Guid),   // 3 - slot 4
                new AssetRef<EnemyActionData>(kneel.Guid),   // 4 - slot 5, forced only (stagger break)
            };
            boss.Passives = new List<AssetRef<EnemyPassiveData>>();
            boss.GlobalActionSlots = new List<int> { 0, 1, 2, 3 };
            // Feather volley comes as a triple: each repeat is a full normal cast (own windup, lanes telegraph and re-aim at
            // the same target) - relentless pressure without closing the dodge lanes.
            boss.ComboChains = new List<BossComboChainData>
            {
                new BossComboChainData
                {
                    TriggerAction = new AssetRef<EnemyActionData>(feather.Guid),
                    RepeatCount = 3,
                    RetargetEachRepeat = false,
                },
            };
            boss.DeferForcedActionsWhileActive = true;
            boss.FallBackWhenTopActionBlocked = true; // a gated tornado/swoop/whistle never stalls the fight - feathers fill in
            boss.DebugPrototype = new AssetRef<EntityPrototype>(bossPrototype.Guid);

            StaggerProfileData stagger = w1Boss.Stagger;
            stagger.ThresholdPercents = w1Boss.Stagger.ThresholdPercents.ToList();
            stagger.OnBreakForcedAction = new AssetRef<EnemyActionData>(kneel.Guid);
            boss.Stagger = stagger;

            // Same four mechanics in both phases. Phase 2 (<= 50%): shorter recovery, slightly quicker windups/moves/
            // feathers.
            boss.Phases = new List<BossPhaseData>
            {
                new BossPhaseData { EntryTrigger = BossPhaseEntryTrigger.HealthThreshold, HealthPercentThreshold = FP._1, ActionPoolSlots = new List<int>() },
                new BossPhaseData
                {
                    EntryTrigger = BossPhaseEntryTrigger.HealthThreshold,
                    HealthPercentThreshold = FP._0_50,
                    ActionPoolSlots = new List<int>(),
                    Modifiers = new BossStatModifiers
                    {
                        RecoveryMultiplier = F("1.6"),
                        AnticipationMultiplier = F("1.15"),
                        MoveSpeedMultiplier = F("1.2"),
                        ProjectileSpeedMultiplier = F("1.15"),
                        // no QuantityMultiplier: the feather lanes telegraph is drawn for exactly FeatherLaneCount shots
                    },
                },
            };

            foreach (Object asset in new Object[] { boss, whistleTelegraph, featherTelegraph, featherProjectile, featherMovement, trashMovement, trashHit,
                         slow, mark, feather, featherDelivery, whistle, whistleDelivery, tornado, tornadoDelivery, swoop, swoopDelivery, kneel, kneelDelivery })
            {
                EditorUtility.SetDirty(asset);
            }

            foreach (ProjectileDataAsset trash in trashProjectiles)
                EditorUtility.SetDirty(trash);

            AssetDatabase.SaveAssets();
            ConfigureBossPrefab(bodyMaterial, boss);
            AssetDatabase.SaveAssets();
            QuantumUnityDBUtilities.RefreshGlobalDB();

            Debug.Log($"[DonoDoCeu] Built Dono do Céu - boss {boss.Guid}, prototype {bossPrototype.Guid}, tornado {tornadoPrototype.Guid}");
        }

        private static string TrashPrefabPath(int i) => $"{ProjectileFolder}/DonoDoCeu_Trash{TrashNames[i]}.prefab";
        private const string FeatherPrefabPath = ProjectileFolder + "/DonoDoCeu_FeatherProjectile.prefab";

        // ------------------------------------------------------------------ sprites / materials

        private static void SliceSheets()
        {
            Slice(SheetAPath, "A", 100);
            Slice(SheetBPath, "B", 100);
            Slice(TrashSheetPath, "T", 100);

            _sprites = new Dictionary<string, Sprite>();
            foreach (string path in new[] { SheetAPath, SheetBPath, TrashSheetPath })
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

#pragma warning disable CS0618 // legacy spritesheet API - same as RiffRiotBossBuilder / RukkElitesSpriteSlicer
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
                throw new System.InvalidOperationException($"[DonoDoCeu] sprite {name} not found");

            return sprite;
        }

        // Own copy of the W1 boss's pink-crystal glow sprite material (same shader) - his Rift crystals glow the same way.
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

        // ------------------------------------------------------------------ feather lanes telegraph

        // Every telegraph is the standard red; what tells the feather volley apart from the whistle's solid cone is the
        // PATTERN: one lane per feather, at the fan's real pellet angles, so it reads as "shots along these lines" and
        // shows the gaps to stand in. A copy of ConeTelegraph90 (same TelegraphFade/TelegraphGrow, same red, same
        // apex-centred 1x1 sprite placement the Cone shape scales by range) with both sprites swapped for lanes.
        private const int FeatherLaneCount = 7;      // = featherDelivery.PelletCount (no phase QuantityMultiplier - lanes must stay true)
        private const float FeatherLaneWidth = 0.7f; // metres, at the telegraph's 12u radius
        private const int LaneTextureSize = 512;
        private const float LaneFadeStart = 0.6f;    // fraction of the radius where the lanes start fading to nothing

        private static GameObject BuildFeatherLanesTelegraph()
        {
            AssetDatabase.DeleteAsset($"{VfxFolder}/DonoDoCeu_WhistleTelegraph.prefab"); // retired: telegraphs stay red

            Sprite faded = EnsureLaneSprite($"{ArtFolder}/DonoDoCeu_FeatherLanesFaded.png", faded: true);
            Sprite opaque = EnsureLaneSprite($"{ArtFolder}/DonoDoCeu_FeatherLanesOpaque.png", faded: false);

            string path = $"{VfxFolder}/DonoDoCeu_FeatherLanesTelegraph.prefab";
            EnsureCopy(Cone90TelegraphPrefabPath, path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                root.GetComponent<SpriteRenderer>().sprite = faded;
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.gameObject != root)
                        renderer.sprite = opaque; // TelegraphGrow's fill
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // White lanes (tinted red by the prefab's SpriteRenderer), apex at the sprite centre, fan pointing +Y, radius =
        // half the sprite - the same layout as Telegraph90Degrees*.png. Faded = translucent lanes; opaque = solid lanes for
        // the grow fill. Both fade out over the last LaneFadeStart..1 of the range - no end cap: the feathers keep flying.
        private static Sprite EnsureLaneSprite(string path, bool faded)
        {
            int size = LaneTextureSize;
            float half = size * 0.5f;
            float laneHalf = FeatherLaneWidth * 0.5f / FeatherRange; // in radius units
            float pixel = 1f / half;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f - half) / half, (y + 0.5f - half) / half);
                    float coverage = 0f;
                    float fade = 0f;

                    for (int i = 0; i < FeatherLaneCount; i++)
                    {
                        float angle = (-45f + 90f * i / (FeatherLaneCount - 1)) * Mathf.Deg2Rad;
                        Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                        float along = Vector2.Dot(p, dir);
                        if (along < 0f || along > 1f)
                            continue;

                        float across = Mathf.Abs(p.x * dir.y - p.y * dir.x);
                        float c = Mathf.Clamp01((laneHalf - across) / pixel + 0.5f);
                        if (c > coverage)
                        {
                            coverage = c;
                            fade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(LaneFadeStart, 1f, along));
                        }
                    }

                    float alpha = (faded ? coverage * 0.45f : coverage) * fade;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size; // 1x1 unit, like the cone sprites
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ trash impact

        // Owned copy of Epic Toon FX's ToonBodySlam (the original stays untouched) as the trash's landing/hit burst:
        // dirty dust ring + hot orange flash and sparks, so a piece of trash lands like a small explosive. Played by
        // EffectsManager at the blast radius (AreaHitData.BlastEffectPrefab).
        // EffectsManager plays an AreaHitData blast at scale = blast radius, so the effect must reach ~1 at scale 1 for the
        // slam to show the real hit area. The pack ToonBodySlam's wave ends at ~2 (measured from its particles), so it's
        // shrunk by TrashImpactSpatialScale: wave/flash end on the 1.6 radius, only the dust spills a little past it.
        private const float TrashImpactSpatialScale = 0.5f;

        private static ParticleSystem BuildTrashImpact()
        {
            return BuildOwnedEffect(ToonBodySlamPath, "DonoDoCeu_TrashImpact", ps => ps.name switch
            {
                "CloudCircle" => new Color(0.4f, 0.34f, 0.28f, 0.9f), // dirty dust kicked up - reads on light floors
                "Sparks" => new Color(1f, 0.6f, 0.2f, 1f),
                _ => new Color(1f, 0.45f, 0.12f, 1f),                 // hot orange flash/wave
            }, customize: root => ScaleFromSource(root, ToonBodySlamPath, TrashImpactSpatialScale));
        }

        // Resizes an owned effect spatially (sizes, speeds, emitter shape, gravity) to `scale` x its PACK source's values,
        // matched by system name - always from the source, so rebuilding never compounds the shrink. Timing is untouched.
        private static void ScaleFromSource(GameObject root, string sourcePath, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var sources = source.GetComponentsInChildren<ParticleSystem>(true).ToDictionary(ps => ps == source.GetComponent<ParticleSystem>() ? "<root>" : ps.name);

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                string key = ps.gameObject == root ? "<root>" : ps.name;
                if (sources.TryGetValue(key, out ParticleSystem from) == false)
                    continue;

                ParticleSystem.MainModule main = ps.main;
                main.startSizeMultiplier = from.main.startSizeMultiplier * scale;
                main.startSizeXMultiplier = from.main.startSizeXMultiplier * scale;
                main.startSizeYMultiplier = from.main.startSizeYMultiplier * scale;
                main.startSizeZMultiplier = from.main.startSizeZMultiplier * scale;
                main.startSpeedMultiplier = from.main.startSpeedMultiplier * scale;
                main.gravityModifierMultiplier = from.main.gravityModifierMultiplier * scale;

                ParticleSystem.ShapeModule shape = ps.shape;
                shape.radius = from.shape.radius * scale;
                shape.scale = from.shape.scale * scale;
                shape.position = from.shape.position * scale;

                ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
                velocity.xMultiplier = from.velocityOverLifetime.xMultiplier * scale;
                velocity.yMultiplier = from.velocityOverLifetime.yMultiplier * scale;
                velocity.zMultiplier = from.velocityOverLifetime.zMultiplier * scale;
                velocity.radialMultiplier = from.velocityOverLifetime.radialMultiplier * scale;
            }
        }

        // Feather volley release: Epic Toon FX's FeatherExplosion in the vulture's black/red with pink rift sparks.
        private static ParticleSystem BuildFeatherBurst()
        {
            return BuildOwnedEffect(FeatherExplosionPath, "DonoDoCeu_FeatherBurst", ps => ps.name switch
            {
                "FeatherExplosion" => new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.1f, 0.1f, 1f), new Color(0.82f, 0.12f, 0.16f, 1f)),
                "Clouds" => new Color(0.22f, 0.2f, 0.22f, 0.7f),
                "Sparks" => new Color(1f, 0.25f, 0.55f, 1f),
                _ => new Color(1f, 0.2f, 0.4f, 0.6f),                 // glow
            });
        }

        // The open beak on the WhistlePrep pose, in sprite units from its pivot (art facing right) - where the charge sits
        // (ParentToBody, so it leaves the mouth whichever way he faces).
        private static readonly Vector3 WhistleMouthOnSprite = new Vector3(0.78f, 2.37f, -0.05f);
        private const float WhistleChargeScale = 0.55f;

        // Whistle charge-up: Epic Toon FX's MagicCharge (streaks + dust pulled in to a glow), all white - air being sucked
        // into the beak. Loops; the windup step stops it. Alpha blended: an additive glow washes out on the bright floors.
        private static ParticleSystem BuildWhistleCharge()
        {
            return BuildOwnedEffect(MagicChargePath, "DonoDoCeu_WhistleCharge", ps => ps.name switch
            {
                "Dust" => new Color(1f, 1f, 1f, 0.9f),
                "Glow" => new Color(1f, 1f, 1f, 0.75f),
                _ => Color.white,                                     // root: the converging streaks
            }, alphaBlended: true);
        }

        // Whistle blast: Epic Toon FX's MusicalNoteExplosion, all white notes/sparks - nothing like the feather release.
        // Carries the referee whistle sound (ParticleSound, OneShot), so the whistle is heard exactly when the cone goes off.
        private static ParticleSystem BuildWhistleBlast()
        {
            return BuildOwnedEffect(MusicalNoteExplosionPath, "DonoDoCeu_WhistleBlast", ps => ps.name switch
            {
                "MusicalNoteExplosion" or "Sparks" => Color.white,
                _ => new Color(1f, 1f, 1f, 0.6f),                     // glow
            }, customize: root => ParticleSoundAuthoring.SetOneShot(root, EnsureWhistleSound()));
        }

        // Enemies-group SoundData for the whistle: the blow (0.1-0.6s) plus the gym reverb tail, trimmed off the silent
        // head and the long silent end, with a short fade.
        private static SoundData EnsureWhistleSound()
        {
            SoundData sound = SoundDataCreator.CreateOrUpdate(WhistleSoundPath, SoundGroup.Enemies, AssetDatabase.LoadAssetAtPath<AudioClip>(WhistleClipPath));
            sound.volume = new Vector2(0.8f, 0.8f);
            sound.pitch = new Vector2(0.98f, 1.02f);
            sound.startAt = 0.08f;
            sound.endAt = 1.5f;
            sound.fadeOut = 0.3f;
            sound.cooldown = 0.5f;
            sound.maxConcurrent = 2;
            return sound;
        }

        // Arena-wide crowd reaction, so not spatial. Several heroes marked by one whistle = one boo (maxConcurrent 1 +
        // cooldown covering the same blast).
        private static SoundData EnsureCrowdBooSound()
        {
            SoundData sound = SoundDataCreator.CreateOrUpdate(CrowdBooSoundPath, SoundGroup.Enemies, AssetDatabase.LoadAssetAtPath<AudioClip>(CrowdBooClipPath));
            sound.volume = new Vector2(0.8f, 0.8f);
            sound.pitch = new Vector2(0.97f, 1.03f);
            sound.spatial = false;
            sound.cooldown = 1f;
            sound.maxConcurrent = 1;
            return sound;
        }

        // Owned copy of a pack effect (the original stays untouched), each system re-tinted. Pack materials on
        // Particles/Standard Unlit (which URP doesn't draw) or other built-ins get an owned Mobile/Particles material on
        // the same texture (_ADD = additive, else alpha blended - or alpha blended for all with `alphaBlended`); a rerun
        // keeps the already-owned ones. `customize` runs on the prefab contents before the single save.
        private static ParticleSystem BuildOwnedEffect(string sourcePath, string name, System.Func<ParticleSystem, ParticleSystem.MinMaxGradient> tint,
            bool alphaBlended = false, System.Action<GameObject> customize = null)
        {
            string path = $"{VfxFolder}/{name}.prefab";
            EnsureCopy(sourcePath, path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    Material source = renderer.sharedMaterial;
                    if (source == null || AssetDatabase.GetAssetPath(source).StartsWith(VfxFolder) == true)
                        continue;

                    bool additive = alphaBlended == false && source.name.EndsWith("_ADD");
                    string materialName = alphaBlended == true ? source.name.Replace("_ADD", "_AB") : source.name;
                    renderer.sharedMaterial = EnsureParticleMaterial($"{VfxFolder}/{name}_{materialName}.mat",
                        additive ? "Mobile/Particles/Additive" : "Mobile/Particles/Alpha Blended", AssetDatabase.GetAssetPath(source.mainTexture));
                }

                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.startColor = tint(ps);
                    WhitenColorOverLifetime(ps);
                }

                customize?.Invoke(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
        }

        // ------------------------------------------------------------------ tornado

        // Owned copy of the Epic Toon FX Whirlwind2 (the original stays untouched), restyled: grey/black wind with
        // restrained red, sparse red/black feathers, a faint dark-red footprint ring the size of the damage radius.
        // Translucent enough that players and warnings stay readable through it.
        private const float WhirlwindScale = 0.85f;
        private const float TornadoFootprintDiameter = 3.2f; // 2 x VendavalDaGeral Radius

        private static GameObject BuildWhirlwindVariant(Material featherMaterial, Material ringMaterial)
        {
            string path = $"{VfxFolder}/DonoDoCeu_Whirlwind2.prefab";
            AssetDatabase.DeleteAsset($"{VfxFolder}/DonoDoCeu_Whirlwind.prefab"); // first pass was built on Whirlwind (1)
            EnsureCopy(WhirlwindPath, path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                root.transform.localScale = Vector3.one * WhirlwindScale;

                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.name == "Feathers" || ps.name == "Footprint")
                        continue;

                    // Darker and denser than the pack's (a pale breeze on a dark demo floor) so the funnel reads on
                    // light floors (grass, concrete) - still translucent enough to see players and warnings through.
                    Color tint = ps.name switch
                    {
                        "Whirlwind2" => new Color(0.2f, 0.2f, 0.24f, 1f),     // the funnel (its texture is already very soft)
                        "VacuumDust" => new Color(0.85f, 0.14f, 0.18f, 1f),   // the restrained red accent
                        _ => new Color(0.25f, 0.25f, 0.3f, 0.6f),           // dust clouds at the base
                    };
                    SetStartColor(ps, tint);
                    WhitenColorOverLifetime(ps);
                }

                ParticleSystem feathers = EnsureChildSystem(root.transform, "Feathers");
                ConfigureLoop(feathers, rate: 3f, lifetime: 1.4f, size: new Vector2(0.35f, 0.6f));
                ParticleSystem.MainModule featherMain = feathers.main;
                featherMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.12f, 0.12f, 0.9f), new Color(0.8f, 0.12f, 0.16f, 0.9f));
                featherMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                featherMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                featherMain.gravityModifier = -0.15f;
                ParticleSystem.ShapeModule featherShape = feathers.shape;
                featherShape.enabled = true;
                featherShape.shapeType = ParticleSystemShapeType.Circle;
                featherShape.radius = 2f;
                feathers.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                ParticleSystem.TextureSheetAnimationModule sheet = feathers.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 4;
                sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 7f);
                ParticleSystem.RotationOverLifetimeModule spin = feathers.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
                feathers.GetComponent<ParticleSystemRenderer>().sharedMaterial = featherMaterial;

                ParticleSystem footprint = EnsureChildSystem(root.transform, "Footprint");
                float footprintSize = TornadoFootprintDiameter / WhirlwindScale; // x root scale = 2 x damage radius
                ConfigureLoop(footprint, rate: 2f, lifetime: 1.2f, size: new Vector2(footprintSize, footprintSize));
                ParticleSystem.MainModule footMain = footprint.main;
                footMain.startColor = new Color(0.78f, 0.06f, 0.1f, 0.85f);
                footMain.startSpeed = 0f;
                ParticleSystem.ShapeModule footShape = footprint.shape;
                footShape.enabled = false;
                footprint.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                ParticleSystem.SizeOverLifetimeModule footSize = footprint.sizeOverLifetime;
                footSize.enabled = true;
                footSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.85f, 1f, 1f));
                ParticleSystem.ColorOverLifetimeModule footColor = footprint.colorOverLifetime;
                footColor.enabled = true;
                footColor.color = Fade(0.15f, 0.6f);
                var footRenderer = footprint.GetComponent<ParticleSystemRenderer>();
                footRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                footRenderer.sharedMaterial = ringMaterial;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // The tornado hazard entity: Transform3D only (no collider - every hit is a floor-gated query in BossTornadoSystem),
        // its view (BossTornadoView) and the whirlwind variant as the funnel.
        private static void BuildTornadoEntity(GameObject whirlwind)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(TornadoEntityPath) != null
                ? PrefabUtility.LoadPrefabContents(TornadoEntityPath)
                : new GameObject("Favela-DonoDoCeuTornado");
            bool loaded = AssetDatabase.LoadAssetAtPath<GameObject>(TornadoEntityPath) != null;

            try
            {
                var prototype = GetOrAdd<QuantumEntityPrototype>(root);
                prototype.TransformMode = QuantumEntityPrototypeTransformMode.Transform3D;
                prototype.PhysicsCollider.IsEnabled = false;
                prototype.PhysicsBody.IsEnabled = false;

                if (root.GetComponent<QuantumEntityView>() == null)
                    root.AddComponent<QuantumEntityView>();

                var view = GetOrAdd<BossTornadoView>(root);

                // Drop any funnel that isn't an instance of the current whirlwind variant - including a missing-prefab
                // leftover (Unity renames those "Funnel (Missing Prefab ...)", so a plain Find("Funnel") misses them).
                for (int c = root.transform.childCount - 1; c >= 0; c--)
                {
                    GameObject child = root.transform.GetChild(c).gameObject;
                    bool stale = PrefabUtility.IsPrefabAssetMissing(child) ||
                        (child.name.StartsWith("Funnel") && PrefabUtility.GetCorrespondingObjectFromSource(child) != whirlwind);

                    if (stale == true)
                        Object.DestroyImmediate(child);
                }

                Transform funnel = root.transform.Find("Funnel");

                if (funnel == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(whirlwind, root.transform);
                    instance.name = "Funnel";
                    funnel = instance.transform;
                }

                var so = new SerializedObject(view);
                so.FindProperty("funnel").objectReferenceValue = funnel;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, TornadoEntityPath);
            }
            finally
            {
                if (loaded)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------ director FX (Resources/DonoDoCeu)

        private static void BuildDirectorFx(Material ringMaterial, Material glowMaterial)
        {
            EnsureCopy(BodySlamPath, $"{DirectorResources}/DonoDoCeu_TornadoPulse.prefab");
            EnsureCopy(AssetDatabase.GUIDToAssetPath(SmokeGuid), $"{DirectorResources}/DonoDoCeu_TornadoDissipate.prefab");

            // Crowd mark: a pulsing red target ring + soft glow under the marked player (looping while the mark lasts).
            string path = $"{DirectorResources}/DonoDoCeu_CrowdMark.prefab";
            var root = new GameObject("DonoDoCeu_CrowdMark");

            try
            {
                ParticleSystem ring = root.AddComponent<ParticleSystem>();
                ConfigureLoop(ring, rate: 2.5f, lifetime: 0.8f, size: new Vector2(1.6f, 1.6f));
                ParticleSystem.MainModule ringMain = ring.main;
                ringMain.startColor = new Color(0.95f, 0.15f, 0.2f, 0.9f);
                ringMain.startSpeed = 0f;
                ParticleSystem.ShapeModule ringShape = ring.shape;
                ringShape.enabled = false;
                ParticleSystem.SizeOverLifetimeModule ringSize = ring.sizeOverLifetime;
                ringSize.enabled = true;
                ringSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1.25f, 1f, 0.75f));
                ParticleSystem.ColorOverLifetimeModule ringColor = ring.colorOverLifetime;
                ringColor.enabled = true;
                ringColor.color = Fade(0.1f, 0.7f);
                var ringRenderer = root.GetComponent<ParticleSystemRenderer>();
                ringRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                ringRenderer.sharedMaterial = ringMaterial;

                ParticleSystem glow = EnsureChildSystem(root.transform, "Glow");
                ConfigureLoop(glow, rate: 8f, lifetime: 0.3f, size: new Vector2(1.5f, 1.5f));
                ParticleSystem.MainModule glowMain = glow.main;
                glowMain.startColor = new Color(1f, 0.15f, 0.25f, 0.35f);
                glowMain.startSpeed = 0f;
                ParticleSystem.ShapeModule glowShape = glow.shape;
                glowShape.enabled = false;
                var glowRenderer = glow.GetComponent<ParticleSystemRenderer>();
                glowRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                glowRenderer.sharedMaterial = glowMaterial;

                // The whistle connected: the crowd boos the marked hero. Fired once as the mark appears (OneShot on this
                // looping ring); the clip lasts the mark's 4.5s and fades itself out.
                ParticleSoundAuthoring.SetOneShot(root, EnsureCrowdBooSound());

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------ projectile prefabs

        // Above-head "the crowd is throwing at YOU" marker for a CrowdMark (BossHazardFxDirector pops it in, bobs it and
        // removes it with the mark): a trash can icon in a pulsing white ring (the whistle's colour), camera-facing.
        private static void BuildCrowdMarkHead(Material ringMaterial)
        {
            string path = $"{DirectorResources}/DonoDoCeu_CrowdMarkHead.prefab";
            var root = new GameObject("DonoDoCeu_CrowdMarkHead");

            try
            {
                root.AddComponent<Billboard>();

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(root.transform, false);
                iconGo.transform.localScale = Vector3.one * 0.55f;
                var icon = iconGo.AddComponent<SpriteRenderer>();
                icon.sprite = Sprite("DonoDoCeu_TrashCan");
                icon.sortingOrder = 30;

                ParticleSystem pulse = EnsureChildSystem(root.transform, "Pulse");
                ConfigureLoop(pulse, rate: 2.5f, lifetime: 0.6f, size: new Vector2(1.3f, 1.3f));
                ParticleSystem.MainModule pulseMain = pulse.main;
                pulseMain.startColor = new Color(1f, 1f, 1f, 0.95f);
                pulseMain.startSpeed = 0f;
                ParticleSystem.ShapeModule pulseShape = pulse.shape;
                pulseShape.enabled = false;
                ParticleSystem.SizeOverLifetimeModule pulseSize = pulse.sizeOverLifetime;
                pulseSize.enabled = true;
                pulseSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));
                ParticleSystem.ColorOverLifetimeModule pulseColor = pulse.colorOverLifetime;
                pulseColor.enabled = true;
                pulseColor.color = Fade(0.05f, 0.6f);
                var pulseRenderer = pulse.GetComponent<ParticleSystemRenderer>();
                pulseRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                pulseRenderer.sharedMaterial = ringMaterial;
                pulseRenderer.sortingOrder = 29;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Crowd trash: the basic enemy projectile's entity (QPrototypeProjectile/ProjectileView, pooled) with its bolt
        // visuals switched off and a camera-facing, tumbling trash sprite instead. Its HasShadow stays - the shadow on the
        // floor tracks where the piece is in its arc.
        private static void ConfigureTrashPrefab(string path, Sprite sprite)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                Transform core = root.transform.Find("BulletMeshSmallFire");
                foreach (ParticleSystemRenderer renderer in core.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.enabled = false;

                Transform holder = EnsureChild(root.transform, "Trash");
                if (holder.GetComponent<Billboard>() == null)
                    holder.gameObject.AddComponent<Billboard>();

                Transform spriteTransform = EnsureChild(holder, "Sprite");
                spriteTransform.localScale = Vector3.one * 0.42f;
                var spriteRenderer = GetOrAdd<SpriteRenderer>(spriteTransform);
                spriteRenderer.sprite = sprite;
                spriteRenderer.sortingOrder = 5;

                var spin = GetOrAdd<ProjectileSpinView>(spriteTransform);
                var so = new SerializedObject(spin);
                so.FindProperty("target").objectReferenceValue = spriteTransform;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Same pooled ground blob as enemies (GroundBlobManager): shrinks/fades with height, so it grows in
                // under the warning as the piece comes down.
                // Exactly one shadow: the copied enemy bullet carries its own faint ones - switched off.
                foreach (HasShadow inherited in root.GetComponentsInChildren<HasShadow>(true))
                    if (inherited.transform != holder)
                        inherited.enabled = false;

                var shadow = GetOrAdd<HasShadow>(holder);
                var shadowSo = new SerializedObject(shadow);
                // Base ~ the 2.4m warning it lands in; alpha x3 because the config fades a blob to 25% above 4m and the
                // throw arcs at 5-9m - it has to read from the very start of the flight.
                shadowSo.FindProperty("baseScale").floatValue = 2.6f;
                shadowSo.FindProperty("shadowAlphaMultiplier").floatValue = 3f;
                shadowSo.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Feather volley shot: the basic enemy projectile, bolt mesh hidden, its trail/glow recoloured red/magenta and one
        // flat feather (Epic Toon FX feather sheet, straight frame) pointing along the flight direction.
        private static void ConfigureFeatherPrefab(string path, Material featherMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                Transform core = root.transform.Find("BulletMeshSmallFire");
                core.GetComponent<ParticleSystemRenderer>().enabled = false;

                foreach (ParticleSystem ps in core.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform == core)
                        continue;

                    SetStartColor(ps, ps.name == "Glow" ? new Color(1f, 0.15f, 0.55f, 0.3f) : new Color(0.9f, 0.15f, 0.2f, 1f));
                    WhitenColorOverLifetime(ps);
                }

                ParticleSystem feather = EnsureChildSystem(root.transform, "Feather");
                ParticleSystem.MainModule main = feather.main;
                main.loop = true;
                main.duration = 1f;
                main.startLifetime = 0.1f;
                main.startSpeed = 0f;
                main.startSize = 1.3f;
                main.maxParticles = 1;
                main.startColor = new Color(0.85f, 0.12f, 0.18f, 1f);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystem.EmissionModule emission = feather.emission;
                emission.rateOverTime = 60f;
                ParticleSystem.ShapeModule shape = feather.shape;
                shape.enabled = false;
                ParticleSystem.TextureSheetAnimationModule sheet = feather.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 4;
                sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(4f / 8f); // straight feather, row 2 col 1
                // Lying flat, its quill (texture +X) along the shot's forward (+Z), facing up.
                feather.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
                var renderer = feather.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                renderer.sharedMaterial = featherMaterial;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ boss prefab

        private static void ConfigureBossPrefab(Material material, BossDataAsset boss)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);

            try
            {
                EnemyViewRig rig = root.GetComponentInChildren<EnemyViewRig>(true);
                rig.gameObject.name = "DonoDoCeu-Body";

                var renderer = rig.GetComponent<SpriteRenderer>();
                renderer.sprite = Sprite("DonoDoCeu_Idle");
                renderer.sharedMaterial = material;

                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("dyingSprite").objectReferenceValue = Sprite("DonoDoCeu_KneelHold");
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                Transform retiredMouth = rig.transform.Find("WhistleMouth"); // the charge now rides the body (ParentToBody)
                if (retiredMouth != null)
                    Object.DestroyImmediate(retiredMouth.gameObject);

                Transform smoke = rig.transform.Find("VFX3D_Smoke"); // W1-only smoke plume
                if (smoke != null)
                    Object.DestroyImmediate(smoke.gameObject);

                var enemyProto = root.GetComponent<QPrototypeEnemy>();
                var so = new SerializedObject(enemyProto);
                SerializedProperty data = so.FindProperty("Prototype.EnemyData.Id.Value");
                if (data == null)
                    throw new System.InvalidOperationException("[DonoDoCeu] QPrototypeEnemy.Prototype.EnemyData not found - serialized layout changed");

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

        private static EntityPrototype LoadPrototype(string prefabPath)
        {
            string protoPath = prefabPath.Replace(".prefab", "EntityPrototype.qprototype");
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            EntityPrototype prototype = AssetDatabase.LoadAllAssetsAtPath(protoPath).OfType<EntityPrototype>().FirstOrDefault();

            if (prototype == null || prototype.Guid.IsValid == false)
                throw new System.InvalidOperationException($"[DonoDoCeu] no EntityPrototype generated for {prefabPath} (expected {protoPath})");

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

            return SaveClone(AssetDatabase.LoadAllAssetsAtPath(templatePath).OfType<T>().First(), name);
        }

        private static T CloneSubOrLoad<T>(string templatePath, string name) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(PathOf(name));
            if (existing != null)
                return existing;

            return SaveClone(AssetDatabase.LoadAllAssetsAtPath(templatePath).OfType<T>().First(), name);
        }

        // Identifier reset so the importer stamps a fresh Quantum guid on the copy.
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
            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null)
                return;

            if (AssetDatabase.CopyAsset(from, to) == false)
                throw new System.InvalidOperationException($"[DonoDoCeu] could not copy {from} -> {to}");
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
        private static T GetOrAdd<T>(Component owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.gameObject.AddComponent<T>();
        }

        private static T GetOrAdd<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
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
            return GetOrAdd<ParticleSystem>(child);
        }

        private static void ConfigureLoop(ParticleSystem ps, float rate, float lifetime, Vector2 size)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = lifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 64;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
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

        private static Gradient Fade(float inUntil, float outFrom)
        {
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inUntil), new GradientAlphaKey(1f, outFrom), new GradientAlphaKey(0f, 1f) });
            return fade;
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

        private static ParticleSystem LoadParticle(string path)
        {
            var particle = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
            if (particle == null)
                Debug.LogWarning($"[DonoDoCeu] particle prefab missing at {path} - step left without it");

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

        // Hover flap: A3/A4 alternating.
        private static AttackVisualStep FlapStep(float duration, AttackVisualParticle particle)
        {
            AttackVisualStep step = Step(AttackAnimationType.None, duration, null, particle);
            step.BodySpriteFrames = new[] { Sprite("DonoDoCeu_HoverUp"), Sprite("DonoDoCeu_HoverDown") };
            step.BodySpriteFrameRate = 5f;
            step.BodySpriteLoop = true;
            return step;
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

        private static AttackVisualParticle Particle(ParticleSystem prefab, Vector3 offset, float scale, bool snap = false)
        {
            return new AttackVisualParticle { ParticlePrefab = prefab, Anchor = ParticleAnchor.OnSelf, Offset = offset, SnapToGround = snap, Scale = scale };
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
