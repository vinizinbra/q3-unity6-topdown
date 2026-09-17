namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Single source of truth for authoring all 12 Hero Mastery assets (2 per hero - Weapon Weight +
    // Element, see docs/hero-mastery.md) and wiring them into each hero's CharacterData.PassiveUpgrades.
    // Every <Hero>AscensionAssetGenerator calls the matching Create*Mastery() pair below instead of
    // authoring its own copy, so a tuned value only ever lives in one place. This file's own menu item
    // ALSO calls them, to regenerate + rewire just the 12 Mastery assets across all 6 heroes without
    // touching any hero's other Ascension lines - useful for iterating on Mastery balance without
    // re-running (and re-risking drift in) a whole hero's 9-line roster each time.
    //
    // WireInto below is deliberately NOT a full-list-replace the way every <Hero>AscensionAssetGenerator.
    // WireCharacterData is - this tool only owns 2 of however many entries a hero's PassiveUpgrades list
    // holds, so it merges (append-if-missing, by the asset's own stable Guid - CreateOrUpdate always
    // reuses the same asset at the same path, so a Mastery asset's Guid never changes across reruns)
    // rather than replacing the whole list.
    //
    // Weapon Family -> Weapon Weight migration: the previous system (ShotgunMastery/PistolMastery/
    // SniperMastery/SmgMastery/AssaultRifleMastery/GrenadeLauncherMastery, one per hero, keyed off
    // WeaponFamily) has been fully replaced, not layered alongside this one - those 6 old .asset files
    // and their stale PassiveUpgrades wiring entries were removed directly (see docs/hero-mastery.md's
    // migration note); this generator only ever authors the 6 new Weight-keyed assets below.
    public static class HeroMasteryAssetGenerator
    {
        private const string BrutePassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/Brute_PassiveSkill/Brute_PassiveSkillUpgrades";
        private const string PixiePassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/Pixie_PassiveSkill/Pixie_PassiveSkillUpgrades";
        private const string MaxPassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Max/Max_PassiveSkill/Max_PassiveSkillUpgrades";
        private const string KaiPassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/Kai_PassiveUpgrades/Kai_PassiveSkillUpgrades";
        private const string ZaraPassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara/Zara_PassiveSkillUpgrades";
        private const string LuxPassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/Lux_PassiveSkill/Lux_PassiveSkillUpgrades";

        private const string BruteCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/BruteCharacterData.asset";
        private const string PixieCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/PixieCharacterData.asset";
        private const string MaxCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/MaxCharacterData.asset";
        private const string KaiCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/KaiCharacterData.asset";
        private const string ZaraCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/ZaraCharacterData.asset";
        private const string LuxCharacterDataPath = "Assets/_QuantumUser/Resources/Characters/LuxCharacterData.asset";

        // Shared per-rank damage curves - every Weapon Weight line and every non-Neutral Element line
        // uses Standard; every Neutral Element line uses the deliberately weaker Neutral curve (Neutral
        // always applies regardless of which weapon is equipped, so it's tuned lower). Centralized here
        // so retuning the whole Mastery system's power curve is a one-line change per curve instead of
        // 12 independent literals.
        private static readonly FP[] StandardDamageMultiplierPerRank = { FP.FromString("0.15"), FP.FromString("0.30"), FP._0_50 };
        private static readonly FP[] NeutralDamageMultiplierPerRank = { FP._0_10, FP._0_20, FP.FromString("0.40") };

        [MenuItem("Tools/RiftRaiders/Hero Mastery/Generate All Mastery Assets")]
        internal static void GenerateAll()
        {
            CreateFolderRecursive(BrutePassiveUpgradesFolderPath);
            CreateFolderRecursive(PixiePassiveUpgradesFolderPath);
            CreateFolderRecursive(MaxPassiveUpgradesFolderPath);
            CreateFolderRecursive(KaiPassiveUpgradesFolderPath);
            CreateFolderRecursive(ZaraPassiveUpgradesFolderPath);
            CreateFolderRecursive(LuxPassiveUpgradesFolderPath);

            var brute = CreateBruteMastery();
            var pixie = CreatePixieMastery();
            var max = CreateMaxMastery();
            var kai = CreateKaiMastery();
            var zara = CreateZaraMastery();
            var lux = CreateLuxMastery();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireInto(BruteCharacterDataPath, brute.weight, brute.element);
            WireInto(PixieCharacterDataPath, pixie.weight, pixie.element);
            WireInto(MaxCharacterDataPath, max.weight, max.element);
            WireInto(KaiCharacterDataPath, kai.weight, kai.element);
            WireInto(ZaraCharacterDataPath, zara.weight, zara.element);
            WireInto(LuxCharacterDataPath, lux.weight, lux.element);

            LogHelper.Log("HeroMasteryAssetGenerator", "Regenerated and rewired Weapon Weight + Element Mastery for all 6 heroes " +
                "(12 assets) - every per-rank value re-set explicitly, every other Ascension/Passive line on each hero left untouched.");
        }

        // -- Brute: Heavy (Weapon Weight) + Neutral (Element) --

        internal static (BruteHeavyMasteryData weight, NeutralMasteryData element) CreateBruteMastery()
        {
            BruteHeavyMasteryData heavyMastery = CreateOrUpdate<BruteHeavyMasteryData>($"{BrutePassiveUpgradesFolderPath}/BruteHeavyMastery.asset", asset =>
            {
                asset.DisplayName = "Heavy Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Brute's Heavy weapons hit harder - and at close range, harder still.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>30%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>50%</color> Heavy Weapon Damage. Deal +<color=#FD3971>25%</color> additional Heavy Weapon Damage to nearby enemies.",
                };
                asset.Weight = WeaponWeight.Heavy;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.PointBlankDamageBonus = FP.FromString("0.25");
                asset.PointBlankRange = 4;
            });

            NeutralMasteryData neutralMastery = CreateOrUpdate<NeutralMasteryData>($"{BrutePassiveUpgradesFolderPath}/NeutralMastery.asset", asset =>
            {
                asset.DisplayName = "Neutral Mastery";
                asset.MaxRank = 3;
                asset.Description = "Neutral weapons hit harder - and at rank 3, Brute turns his Charge into raw offense.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>10%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>20%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>40%</color> Neutral Weapon Damage. Neutral weapons gain +<color=#FD3971>30%</color> Knockback, and Brute deals +<color=#FD3971>30%</color> Damage while Charged.",
                };
                asset.Element = ElementType.Neutral;
                asset.DamageMultiplierPerRank = NeutralDamageMultiplierPerRank;
                asset.NeutralKnockbackBonus = FP.FromString("0.30");
                asset.ChargedDamageBonus = FP.FromString("0.30");
            });

            return (heavyMastery, neutralMastery);
        }

        // -- Pixie: Heavy (Weapon Weight) + Fire (Element) --

        internal static (PixieHeavyMasteryData weight, PixieFireMasteryData element) CreatePixieMastery()
        {
            PixieHeavyMasteryData heavyMastery = CreateOrUpdate<PixieHeavyMasteryData>($"{PixiePassiveUpgradesFolderPath}/PixieHeavyMastery.asset", asset =>
            {
                asset.DisplayName = "Heavy Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Pixie's Heavy weapons hit harder, and at rank 3, hit a wider area.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>30%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>50%</color> Heavy Weapon Damage. Heavy weapon attacks hit in a <color=#FD3971>2m</color> wider area.",
                };
                asset.Weight = WeaponWeight.Heavy;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.ExtraRadius = 2;
            });

            // Incendiary Rounds (Fire Mastery R3) - a real, dedicated AreaHitData explosion (same asset
            // type Grenade Launcher's own weapon uses) that every Fire-weapon hit detonates, guaranteed.
            // The DamageEffectData sub-asset is what actually turns "detonate" into damage - see
            // HitEffectUtility.ApplyInRadius's own comment on why an Effects-less AreaHitData deals 0.
            var incendiaryRoundsDamage = CreateOrUpdate<DamageEffectData>($"{PixiePassiveUpgradesFolderPath}/PixieFireMasteryExplosionDamage.asset", asset =>
            {
                asset.DamageMultiplier = FP.FromString("0.30");
            });

            var incendiaryRoundsExplosion = CreateOrUpdate<AreaHitData>($"{PixiePassiveUpgradesFolderPath}/PixieFireMasteryExplosion.asset", asset =>
            {
                asset.BlastRadius = FP.FromString("2.5");
                asset.TargetMask = DamageTargetMask.Enemies;
                asset.TriggersSpawnUpgrades = true;
                asset.Effects = new List<AssetRef<HitEffectData>> { new AssetRef<HitEffectData>(incendiaryRoundsDamage.Guid) };
            });

            PixieFireMasteryData fireMastery = CreateOrUpdate<PixieFireMasteryData>($"{PixiePassiveUpgradesFolderPath}/PixieFireMastery.asset", asset =>
            {
                asset.DisplayName = "Fire Mastery";
                asset.MaxRank = 3;
                asset.Description = "Fire weapons hit harder, and at rank 3, every Fire-weapon hit also detonates a small area explosion.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Fire Weapon Damage.",
                    "+<color=#FD3971>30%</color> Fire Weapon Damage.",
                    "+<color=#FD3971>50%</color> Fire Weapon Damage. Fire weapon hits detonate a small area explosion, dealing <color=#FD3971>30%</color> Damage in a <color=#FD3971>2.5m</color> radius.",
                };
                asset.Element = ElementType.Fire;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.ExplosiveShotArea = new AssetRef<AreaHitData>(incendiaryRoundsExplosion.Guid);
            });

            return (heavyMastery, fireMastery);
        }

        // -- Max: Light (Weapon Weight) + Fire (Element) --

        internal static (MaxLightMasteryData weight, MaxFireMasteryData element) CreateMaxMastery()
        {
            MaxLightMasteryData lightMastery = CreateOrUpdate<MaxLightMasteryData>($"{MaxPassiveUpgradesFolderPath}/MaxLightMastery.asset", asset =>
            {
                asset.DisplayName = "Light Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Max's Light weapons hit harder, and at rank 3, punish whoever he's marked for Vendetta.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Light Weapon Damage.",
                    "+<color=#FD3971>30%</color> Light Weapon Damage.",
                    "+<color=#FD3971>50%</color> Light Weapon Damage. Light weapons deal +<color=#FD3971>25%</color> Damage to Vendetta-marked enemies.",
                };
                asset.Weight = WeaponWeight.Light;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.VendettaDamageBonus = FP.FromString("0.25");
            });

            MaxFireMasteryData maxFireMastery = CreateOrUpdate<MaxFireMasteryData>($"{MaxPassiveUpgradesFolderPath}/MaxFireMastery.asset", asset =>
            {
                asset.DisplayName = "Fire Mastery";
                asset.MaxRank = 3;
                asset.Description = "Fire weapons hit harder, and at rank 3, hit hardest while Max is Overdriven.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Fire Weapon Damage.",
                    "+<color=#FD3971>30%</color> Fire Weapon Damage.",
                    "+<color=#FD3971>50%</color> Fire Weapon Damage. Fire weapons deal +<color=#FD3971>30%</color> Damage during Overdrive.",
                };
                asset.Element = ElementType.Fire;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.InfernalRageDamageBonus = FP.FromString("0.30");
            });

            return (lightMastery, maxFireMastery);
        }

        // -- Kai: Heavy (Weapon Weight) + Neutral (Element) --

        internal static (KaiHeavyMasteryData weight, KaiNeutralMasteryData element) CreateKaiMastery()
        {
            KaiHeavyMasteryData heavyMastery = CreateOrUpdate<KaiHeavyMasteryData>($"{KaiPassiveUpgradesFolderPath}/KaiHeavyMastery.asset", asset =>
            {
                asset.DisplayName = "Heavy Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Kai's Heavy weapons hit harder, and the opening shot on a fresh target hits hardest.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>30%</color> Heavy Weapon Damage.",
                    "+<color=#FD3971>50%</color> Heavy Weapon Damage. The first Heavy-weapon hit against each enemy deals +<color=#FD3971>30%</color> Damage.",
                };
                asset.Weight = WeaponWeight.Heavy;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.DeadeyeDamageBonus = FP.FromString("0.30");
            });

            // Ghost Shot (R3) - presentation config asset. EffectPrefab is a Unity-only field
            // (DamageEchoVisualData.View.cs) this generator can't meaningfully author - left
            // unassigned here for hand-authoring in the Inspector; EffectsManager.OnDamageEchoTriggered
            // falls back to its own default area blast effect until one is assigned.
            var ghostShotVisual = CreateOrUpdate<DamageEchoVisualData>($"{KaiPassiveUpgradesFolderPath}/GhostShotVisual.asset", asset => { });

            // Ghost Shot (R3) - the Hit behavior substituted onto the echo projectile in place of
            // whatever weapon fired it (see Projectile.HitOverride's own comment). The echo itself
            // spawns using the OWNER'S CURRENTLY EQUIPPED weapon's own ProjectileDataAsset at runtime
            // (DamageEchoSystem.SpawnEchoProjectile) - same Prototype/Movement as a normal shot from
            // that weapon - so there is nothing weapon-specific to author here. Delivers the already-
            // resolved echo Damage on impact - no Effects list, no re-resolution, no recursion (see
            // EchoHitData's own comment).
            var ghostShotHit = CreateOrUpdate<EchoHitData>($"{KaiPassiveUpgradesFolderPath}/GhostShotHit.asset", asset => { });

            KaiNeutralMasteryData neutralMastery = CreateOrUpdate<KaiNeutralMasteryData>($"{KaiPassiveUpgradesFolderPath}/NeutralMastery.asset", asset =>
            {
                asset.DisplayName = "Neutral Mastery";
                asset.MaxRank = 3;
                asset.Description = "Neutral weapons hit harder, and at rank 3, the first shot of every magazine is echoed by a ghostly repeat.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>10%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>20%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>40%</color> Neutral Weapon Damage. The first shot fired from each magazine is echoed a moment later, dealing <color=#FD3971>50%</color> of its Damage.",
                };
                asset.Element = ElementType.Neutral;
                asset.DamageMultiplierPerRank = NeutralDamageMultiplierPerRank;
                asset.GhostShotDamageMultiplier = FP._0_50;
                asset.GhostShotDelay = FP.FromString("0.2");
                asset.GhostShotVisual = new AssetRef<DamageEchoVisualData>(ghostShotVisual.Guid);
                asset.GhostShotHit = new AssetRef<ProjectileHitData>(ghostShotHit.Guid);
            });

            return (heavyMastery, neutralMastery);
        }

        // -- Zara: Light (Weapon Weight) + Electric (Element) --

        internal static (ZaraLightMasteryData weight, ZaraElectricMasteryData element) CreateZaraMastery()
        {
            ZaraLightMasteryData lightMastery = CreateOrUpdate<ZaraLightMasteryData>($"{ZaraPassiveUpgradesFolderPath}/ZaraLightMastery.asset", asset =>
            {
                asset.DisplayName = "Light Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Zara's Light weapons hit harder, and at rank 3, fire faster while she's in Flow.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Light Weapon Damage.",
                    "+<color=#FD3971>30%</color> Light Weapon Damage.",
                    "+<color=#FD3971>50%</color> Light Weapon Damage. While Flow is Active, Light weapons gain +<color=#FD3971>20%</color> Fire Rate.",
                };
                asset.Weight = WeaponWeight.Light;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.FullTempoFireRateBonus = FP._0_20;
            });

            ZaraElectricMasteryData zaraElectricMastery = CreateOrUpdate<ZaraElectricMasteryData>($"{ZaraPassiveUpgradesFolderPath}/ZaraElectricMastery.asset", asset =>
            {
                asset.DisplayName = "Electric Mastery";
                asset.MaxRank = 3;
                asset.Description = "Electric weapons hit harder, and at rank 3, applying Jolt charges Zara up too.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Electric Weapon Damage.",
                    "+<color=#FD3971>30%</color> Electric Weapon Damage.",
                    "+<color=#FD3971>50%</color> Electric Weapon Damage. Applying Jolt grants +<color=#FD3971>15%</color> Fire Rate for <color=#FD3971>2s</color>.",
                };
                asset.Element = ElementType.Lightning;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.HighVoltageFireRateBonus = FP.FromString("0.15");
                asset.HighVoltageDuration = 2;
            });

            return (lightMastery, zaraElectricMastery);
        }

        // -- Lux: Medium (Weapon Weight) + Neutral (Element) --

        internal static (LuxMediumMasteryData weight, LuxNeutralMasteryData element) CreateLuxMastery()
        {
            LuxMediumMasteryData mediumMastery = CreateOrUpdate<LuxMediumMasteryData>($"{LuxPassiveUpgradesFolderPath}/LuxMediumMastery.asset", asset =>
            {
                asset.DisplayName = "Medium Weapon Mastery";
                asset.MaxRank = 3;
                asset.Description = "Lux's Medium weapons hit harder, and at rank 3, paint targets for her Sentries to finish.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>15%</color> Medium Weapon Damage.",
                    "+<color=#FD3971>30%</color> Medium Weapon Damage.",
                    "+<color=#FD3971>50%</color> Medium Weapon Damage. Enemies hit by Lux's Medium weapon take +<color=#FD3971>25%</color> Damage from her Sentry.",
                };
                asset.Weight = WeaponWeight.Medium;
                asset.DamageMultiplierPerRank = StandardDamageMultiplierPerRank;
                asset.TargetingLinkSentryDamageBonus = FP.FromString("0.25");
                asset.TargetingLinkMarkDuration = 4;
            });

            LuxNeutralMasteryData neutralMastery = CreateOrUpdate<LuxNeutralMasteryData>($"{LuxPassiveUpgradesFolderPath}/LuxNeutralMastery.asset", asset =>
            {
                asset.DisplayName = "Neutral Mastery";
                asset.MaxRank = 3;
                asset.Description = "Neutral weapons hit harder, and at rank 3, mark enemies for her Sentries to focus fire.";
                asset.RankDescriptions = new[]
                {
                    "+<color=#FD3971>10%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>20%</color> Neutral Weapon Damage.",
                    "+<color=#FD3971>40%</color> Neutral Weapon Damage. Neutral weapon hits make Lux's Sentries prioritize that enemy while it is in range.",
                };
                asset.Element = ElementType.Neutral;
                asset.DamageMultiplierPerRank = NeutralDamageMultiplierPerRank;
                asset.FocusDuration = FP._2;
            });

            return (mediumMastery, neutralMastery);
        }

        private static T CreateOrUpdate<T>(string path, System.Action<T> configure) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            bool isNew = existing == null;
            T asset = isNew ? (T)ScriptableObject.CreateInstance(typeof(T)) : existing;

            configure(asset);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        // Merges (append-if-missing by Guid), never replaces the whole list - see the class comment on
        // why this differs from every <Hero>AscensionAssetGenerator.WireCharacterData.
        private static void WireInto(string characterDataPath, PassiveUpgradeData weightMastery, PassiveUpgradeData elementMastery)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(characterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("HeroMasteryAssetGenerator", $"No CharacterData asset at {characterDataPath} - Mastery assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.PassiveUpgrades ??= new List<AssetRef<PassiveUpgradeData>>();

            AppendIfMissing(characterData.PassiveUpgrades, weightMastery);
            AppendIfMissing(characterData.PassiveUpgrades, elementMastery);

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        private static void AppendIfMissing(List<AssetRef<PassiveUpgradeData>> list, PassiveUpgradeData asset)
        {
            if (list.Any(a => a.Id.Value == asset.Guid.Value) == true)
                return;

            list.Add(new AssetRef<PassiveUpgradeData>(asset.Guid));
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
