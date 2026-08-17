namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Zara's Resonance base passive and her 10 Ascension lines (Amplifier/Healing Chorus/
    // Double Time/Main Stage on the Totem Hero Skill; Faster Tempo/Heavy Bass/Restorative Beat/Remix
    // as Passives; Afterbeat/Portable Speaker as Dash Ascensions - see docs/zara-ascensions.md), then
    // wires all of it into ZaraCharacterData.asset and ZaraBaseSkill.asset. Replaces
    // ZaraResonanceAssetGenerator.cs - same "one generator fully replaces every list it touches end to
    // end" fix Brute/Max/Pixie/Kai's own refactors already applied for the identical append-vs-replace
    // drift bug (the old generator's own WireCharacterData only appended-and-deduped DashSkillUpgrades,
    // which is exactly why the old, broken PortableSpeaker.asset survived every prior regeneration).
    //
    // Amplifier/Healing Chorus/Double Time/Main Stage are SkillActionData living on
    // ZaraBaseSkill.Actions (Activated = false), NOT PassiveUpgradeData - same "Hero Skill Ascension"
    // shape every other hero's own refactor already established.
    //
    // Per-rank tuned values ARE explicitly set here on every run, even though every ranked ascension
    // class already carries a matching C# field-initializer default - that default only applies to a
    // BRAND NEW object, not one that already existed before a field's TYPE changed shape (a plain FP
    // becoming FP[]). Explicitly setting every array here every run is what makes CreateOrUpdate
    // idempotent and correct for pre-existing assets, not just newly created ones.
    public static class ZaraAscensionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara/Zara_PassiveSkillUpgrades";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_HeroSkill/Zara_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_DashSkillUpgrades";
        private const string SharedEffectsFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_SharedEffects";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/ZaraCharacterData.asset";
        private const string ZaraBaseSkillPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_HeroSkill/ZaraBaseSkill.asset";
        private const string HitEffectsFolderPath = "Assets/_QuantumUser/Resources/HitEffects";
        // ZaraSpeaker.prefab is the Totem's own real placed entity (already has the correct
        // AreaDamage+AlternatingArea setup) - reused directly as Portable Speaker's own spawn
        // prototype too, rather than a dedicated one, same "Dash mini-version reuses the Hero
        // Skill's own entity" precedent Kai's Warp Wake already established (cosmetic follow-up, not
        // a functional gap). ZaraDeviceSpeaker.prefab is NOT this - it's the THROWN PROJECTILE visual
        // that flies from Zara to the Totem's landing spot (ZaraThrowProjectileSpeaker.Prototype),
        // confirmed by the user - do not point anything at it for Portable Speaker.
        private const string PortableSpeakerPrototypePath = "Assets/_QuantumUser/Resources/Skills/Zara/ZaraEntities/ZaraSpeaker.prefab";

        [MenuItem("Tools/RiftRaiders/Zara/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);
            CreateFolderRecursive(SharedEffectsFolderPath);

            // Shared generic effect assets - authored once, referenced by whichever ranks/lines need
            // them (Healing Chorus/Restorative Beat share the same short-Haste asset, Encore reuses
            // the same overheal-to-Shield asset Restorative Beat's own code path re-derives inline).
            var scaledHeal = CreateOrUpdate<ScaledHealEffectData>($"{SharedEffectsFolderPath}/ZaraScaledHealPulse.asset", a => a.HealMultiplier = FP._1);
            var overhealShield = CreateOrUpdate<OverhealToShieldEffectData>($"{SharedEffectsFolderPath}/ZaraOverhealToShieldEffectData.asset", a =>
            {
                a.ShieldConversionPercent = FP._0_50;
                a.OvershieldCapMultiplier = FP._1_50;
            });
            var shortHaste = CreateOrUpdate<TimedHasteEffectData>($"{SharedEffectsFolderPath}/ZaraShortHasteEffectData.asset", a =>
            {
                a.Duration = FP._2;
                a.AttackSpeedMultiplier = FP._1_50;
            });
            var amplifierKnockback = CreateOrUpdate<KnockbackEffectData>($"{SharedEffectsFolderPath}/AmplifierKnockback.asset", a => a.Tier = KnockbackTier.Small);
            var bassDropStun = LoadHitEffect("StunEffectData");

            // Remix pool - reuses the same shared, zero-config HitEffectData instances every other
            // status source in the game already reads (Burn/Slow/Stun/Rift Mark all pull their own
            // magnitudes from RuntimeConfig.EffectConfig/ElementalReactionConfig), rather than
            // authoring Remix-specific variants.
            var remixPool = new List<RemixPoolEntry>
            {
                new RemixPoolEntry { Effect = LoadHitEffect("SlowEffectData"), Rank2DurationMultiplier = FP._1_50, Rank2MagnitudeMultiplier = FP._1_50 },
                new RemixPoolEntry { Effect = LoadHitEffect("BurnEffectData"), Rank2DurationMultiplier = FP._1_50, Rank2MagnitudeMultiplier = FP._1_50 },
                new RemixPoolEntry { Effect = LoadHitEffect("StunEffectData"), Rank2DurationMultiplier = FP._1_50, Rank2MagnitudeMultiplier = FP._1 },
                new RemixPoolEntry { Effect = LoadHitEffect("RiftMarkEffectData"), Rank2DurationMultiplier = FP._1_50, Rank2MagnitudeMultiplier = FP._1_50 },
            };

            // Passive base - live-tuned values (Max=500/Radius=3/HealPercent=0.05/DamageAmount=10),
            // not the stale C# class field-initializer defaults the old generator used to author.
            ResonancePassiveData passive = CreateOrUpdate<ResonancePassiveData>($"{PassivesFolderPath}/ResonancePassiveData.asset", a =>
            {
                a.Max = 500;
                a.GenerationPerDamage = FP._1;
                a.Radius = FP._3;
                a.HealPercent = FP._0_05;
                a.DamageAmount = 10;
                a.KnockbackTier = KnockbackTier.Small;
            });

            // 4 Hero Skill lines (ranked SkillActionData, wired into ZaraBaseSkill.Actions below)
            AmplifierSkillAction amplifier = CreateOrUpdate<AmplifierSkillAction>($"{HeroSkillUpgradesFolderPath}/AmplifierSkillAction.asset", a =>
            {
                a.DisplayName = "Amplifier";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Totem Damage Beats deal more damage.";
                a.RankDescriptions = new[]
                {
                    "Totem Damage Beats deal 30% more damage.",
                    "Damage Beats deal 60% more damage and knock enemies back.",
                    "Damage Beats deal double damage, and every third Damage Beat Stuns enemies.",
                };
                a.DamageBonus = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                a.KnockbackEffect = amplifierKnockback;
                a.StunInterval = new byte[] { 0, 0, 3 };
                a.StunEffect = bassDropStun;
            });

            HealingChorusSkillAction healingChorus = CreateOrUpdate<HealingChorusSkillAction>($"{HeroSkillUpgradesFolderPath}/HealingChorusSkillAction.asset", a =>
            {
                a.DisplayName = "Healing Chorus";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Totem Healing Beats restore more Health.";
                a.RankDescriptions = new[]
                {
                    "Totem Healing Beats restore 30% more Health.",
                    "Healing Beats restore 60% more Health and briefly Haste allies they heal.",
                    "Healing Beats restore double Health, and 50% of excess healing becomes Shield.",
                };
                a.HealBonus = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                a.HasteEffect = shortHaste;
                a.HealEffectAsset = new AssetRef<HitEffectData>[] { scaledHeal, scaledHeal, overhealShield };
            });

            DoubleTimeSkillAction doubleTime = CreateOrUpdate<DoubleTimeSkillAction>($"{HeroSkillUpgradesFolderPath}/DoubleTimeSkillAction.asset", a =>
            {
                a.DisplayName = "Double Time";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Totem Beats occur more often.";
                a.RankDescriptions = new[]
                {
                    "Totem Beats occur every 0.85s.",
                    "Totem Beats occur every 0.70s.",
                    "Totem Beats occur every 0.5s.",
                };
                a.BeatInterval = new[] { FP.FromString("0.85"), FP.FromString("0.70"), FP._0_50 };
            });

            MainStageSkillAction mainStage = CreateOrUpdate<MainStageSkillAction>($"{HeroSkillUpgradesFolderPath}/MainStageSkillAction.asset", a =>
            {
                a.DisplayName = "Main Stage";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Increase Totem Beat radius.";
                a.RankDescriptions = new[]
                {
                    "Increase Totem Beat radius by 30%.",
                    "Totem grows 50% larger and lasts 2s longer.",
                    "Totem becomes a massive Main Stage, opening with an immediate Damage Beat and ending with a final Healing Beat.",
                };
                a.RadiusBonus = new[] { FP.FromString("0.30"), FP._0_50, FP.FromString("0.75") };
                a.DurationBonus = new[] { FP._0, FP._2, FP._2 };
            });

            // 4 Passive lines (ranked PassiveUpgradeData)
            FasterTempoPassiveUpgradeData fasterTempo = CreateOrUpdate<FasterTempoPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/FasterTempo.asset", a =>
            {
                a.DisplayName = "Faster Tempo";
                a.MaxRank = 3;
                a.Description = "Generate Resonance faster.";
                a.RankDescriptions = new[]
                {
                    "Generate Resonance 25% faster.",
                    "Generate Resonance 50% faster.",
                    "Generate Resonance 75% faster and retain 20% Resonance after each Resonance Pulse.",
                };
                a.BaseGenerationPerDamage = FP._1;
                a.GenerationBonus = new[] { FP._0_25, FP._0_50, FP.FromString("0.75") };
                a.RetainFraction = new[] { FP._0, FP._0, FP._0_20 };
            });

            HeavyBassPassiveUpgradeData heavyBass = CreateOrUpdate<HeavyBassPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/HeavyBass.asset", a =>
            {
                a.DisplayName = "Heavy Bass";
                a.MaxRank = 3;
                a.Description = "Resonance Pulse deals more damage.";
                a.RankDescriptions = new[]
                {
                    "Resonance Pulse deals 50% more damage.",
                    "Resonance Pulse deals 75% more damage and knocks enemies back much harder.",
                    "Resonance Pulse deals double damage and releases a second damaging shockwave shortly afterward.",
                };
                a.BaseDamageAmount = 10;
                a.DamageBonus = new[] { FP._0_50, FP.FromString("0.75"), FP._1 };
                a.KnockbackTierByRank = new[] { KnockbackTier.Small, KnockbackTier.Medium, KnockbackTier.Strong };
                a.SubwooferDamagePercent = new[] { FP._0, FP._0, FP._0_50 };
                a.SubwooferDelay = FP.FromString("0.4");
                a.SubwooferRadiusMultiplier = FP._1;
            });

            RestorativeBeatPassiveUpgradeData restorativeBeat = CreateOrUpdate<RestorativeBeatPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/RestorativeBeat.asset", a =>
            {
                a.DisplayName = "Restorative Beat";
                a.MaxRank = 3;
                a.Description = "Resonance Pulse heals nearby allies for more of their Max Health.";
                a.RankDescriptions = new[]
                {
                    "Resonance Pulse heals nearby allies for 7.5% of their Max Health.",
                    "Resonance Pulse heals 10% Max Health and briefly Hastes nearby allies.",
                    "Resonance Pulse heals 12.5% Max Health and converts excess healing into Shield.",
                };
                a.HealPercent = new[] { FP.FromString("0.075"), FP._0_10, FP.FromString("0.125") };
                a.HasteDuration = new[] { FP._0, FP._2, FP._2 };
                a.HasteMultiplier = new[] { FP._0, FP._1_50, FP._1_50 };
                a.ShieldConversionPercent = new[] { FP._0, FP._0, FP._0_50 };
                a.OvershieldCapMultiplier = FP._1_50;
            });

            RemixPassiveUpgradeData remix = CreateOrUpdate<RemixPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Remix.asset", a =>
            {
                a.DisplayName = "Remix";
                a.MaxRank = 3;
                a.Description = "Every third Resonance Pulse also applies a random status effect to enemies caught in it.";
                a.RankDescriptions = new[]
                {
                    "Every third Resonance Pulse applies a random status effect to enemies hit.",
                    "Remix effects become stronger and last longer.",
                    "Every third Resonance Pulse applies two different random status effects to enemies hit.",
                };
                a.Effects = remixPool;
            });

            // 2 Dash lines
            AfterbeatSkillAction afterbeat = CreateOrUpdate<AfterbeatSkillAction>($"{DashUpgradesFolderPath}/AfterbeatSkillAction.asset", a =>
            {
                a.DisplayName = "Afterbeat";
                a.MaxRank = 3;
                // Explicit, not just the class's own constructor default - CreateOrUpdate re-uses a
                // pre-existing asset instance rather than constructing a fresh one, so a stale
                // serialized Phase (e.g. an accidental Inspector edit, exactly what happened to this
                // asset once already - see docs/zara-ascensions.md's "Corrections" section) would
                // otherwise silently survive every future regeneration.
                a.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                a.Description = "Dashing generates Resonance.";
                a.RankDescriptions = new[]
                {
                    "Dashing generates 20% of your Resonance threshold.",
                    "1s after Dashing, an Afterbeat erupts from your starting position, damaging and knocking back nearby enemies.",
                    "Dashing creates Afterbeats at both ends of the Dash. Enemies hit generate additional Resonance.",
                };
                a.ResonancePercentOnDash = FP._0_20;
                a.Delay = FP._1;
                a.DamagePercentOfSkill = new[] { FP._0, FP.FromString("0.75"), FP.FromString("0.75") };
                a.Radius = new[] { FP._0, FP._4, FP._4 };
                a.KnockbackForce = new[] { FP._0, FP._6, FP._6 };
                a.ResonancePerEnemyHit = 5;
                a.MaxResonancePerDash = 30;
            });

            AssetRef<HitEffectData> damageEffect = ConfigureTotemThrow(new AssetRef<HitEffectData>(scaledHeal.Guid));
            AssetRef<EntityPrototype> speakerPrototype = ResolvePortableSpeakerPrototype();

            PortableSpeakerSkillAction portableSpeaker = CreateOrUpdate<PortableSpeakerSkillAction>($"{DashUpgradesFolderPath}/PortableSpeakerSkillAction.asset", a =>
            {
                a.DisplayName = "Portable Speaker";
                a.MaxRank = 3;
                // Explicit, not just the class's own constructor default - see AfterbeatSkillAction's
                // identical comment above for why this matters on every future regeneration, not just
                // the first.
                a.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                a.Description = "Dashing leaves behind a Portable Speaker that alternates damaging and healing Beats.";
                a.RankDescriptions = new[]
                {
                    "Dashing leaves behind a Portable Speaker that alternates damaging and healing Beats.",
                    "Portable Speaker lasts longer and covers a larger area. Dash ending also heals nearby allies.",
                    "Portable Speaker inherits part of your Totem Ascensions, turning each Dash into a mobile extension of your build.",
                };
                a.Prototype = speakerPrototype;
                a.Duration = new[] { FP._3, FP._4, FP._4 };
                a.BaseRadius = FP._3;
                a.RadiusMultiplier = new[] { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
                a.BeatInterval = FP._1;
                a.TotemBaseDamage = 10;
                a.TotemBaseHealPercent = FP._0_10;
                a.DamagePercentOfTotem = FP._0_50;
                a.HealPercentOfTotem = FP._0_50;
                a.DamageEffect = damageEffect;
                a.HealEffect = new AssetRef<HitEffectData>(scaledHeal.Guid);
                a.DashEndHealPercent = new[] { FP._0, FP._0_05, FP._0_05 };
                a.DashEndHealRadius = FP._5;
                a.MobileStageInheritanceFraction = FP._0_50;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { fasterTempo, heavyBass, restorativeBeat, remix },
                new List<SkillActionData> { afterbeat, portableSpeaker });

            WireTotemActions(new List<SkillActionData> { amplifier, healingChorus, doubleTime, mainStage });

            LogHelper.Log("ZaraAscensionAssetGenerator", "Resonance passive + 10 Ascension lines authored and wired (4 Passive Upgrades " +
                      "into ZaraCharacterData.PassiveUpgrades, Afterbeat/Portable Speaker into ZaraCharacterData.DashSkillUpgrades, " +
                      "Amplifier/Healing Chorus/Double Time/Main Stage into ZaraBaseSkill.Actions as Hero Skill Ascensions - every list " +
                      "fully replaced, not appended; every per-rank value is re-set explicitly on every run). Portable Speaker's Prototype " +
                      "is ZaraSpeaker.prefab (the Totem's own placed entity, reused directly).");
        }

        // Configures the embedded projectile-chain sub-objects inside ZaraBaseSkill.asset
        // (SpawnAlternatingAreaEffectData's own base Damage/Heal amounts, and the shared
        // ZaraScaledHealPulse asset in its HealEffects) - these aren't separate .asset files, so
        // CreateOrUpdate<T> doesn't apply; they're found by scanning the file's own sub-objects, same
        // as KaiAscensionAssetGenerator.ResolveKaiVortexPrototype. Returns the embedded
        // ZaraVoidPulseDamage's own AssetRef so Portable Speaker's DamageEffect can reuse the exact
        // same asset rather than duplicating it.
        private static AssetRef<HitEffectData> ConfigureTotemThrow(AssetRef<HitEffectData> scaledHealEffect)
        {
            var subObjects = AssetDatabase.LoadAllAssetsAtPath(ZaraBaseSkillPath);

            SpawnAlternatingAreaEffectData spawnArea = null;
            DamageEffectData voidPulseDamage = null;

            foreach (var obj in subObjects)
            {
                if (obj is SpawnAlternatingAreaEffectData area)
                {
                    spawnArea = area;
                }
                else if (obj is DamageEffectData damage && obj.name == "ZaraVoidPulseDamage")
                {
                    voidPulseDamage = damage;
                }
            }

            if (voidPulseDamage == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No embedded ZaraVoidPulseDamage found in {ZaraBaseSkillPath} - Totem/Portable Speaker Damage Beats will deal 0 damage until DamageEffect is assigned by hand.");
                return default;
            }

            AssetRef<HitEffectData> damageEffect = new AssetRef<HitEffectData>(voidPulseDamage.Guid);

            if (spawnArea == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No embedded SpawnAlternatingAreaEffectData found in {ZaraBaseSkillPath} - Totem's own base Damage/Heal amounts were not (re)configured.");
                return damageEffect;
            }

            spawnArea.TickInterval = FP._1;
            spawnArea.HealTargetMask = DamageTargetMask.Players;
            spawnArea.HealEffects = new List<AssetRef<HitEffectData>> { scaledHealEffect };
            spawnArea.HealAmount = FP._0_10;
            spawnArea.DamageAmount = 10;
            spawnArea.DamageMask = DamageTargetMask.Enemies;
            spawnArea.DamageEffects = new List<AssetRef<HitEffectData>> { damageEffect };
            EditorUtility.SetDirty(spawnArea);
            AssetDatabase.SaveAssets();

            return damageEffect;
        }

        // Portable Speaker's spawn prototype - reuses ZaraSpeaker.prefab (the Totem's own real placed
        // entity) directly rather than a dedicated prefab - see PortableSpeakerPrototypePath's own
        // comment for why ZaraDeviceSpeaker.prefab is NOT this. Loaded directly from the prefab path
        // (same AssetDatabase.LoadAssetAtPath<EntityPrototype> pattern MaxAscensionAssetGenerator/
        // HeroQuickPlayToolbar already use) rather than reading a stale numeric AssetGuid off the old,
        // deleted PortableSpeaker.asset.
        private static AssetRef<EntityPrototype> ResolvePortableSpeakerPrototype()
        {
            var prototype = AssetDatabase.LoadAssetAtPath<EntityPrototype>(PortableSpeakerPrototypePath);

            if (prototype == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No EntityPrototype at {PortableSpeakerPrototypePath} - Portable Speaker won't spawn anything until Prototype is assigned by hand.");
                return default;
            }

            return new AssetRef<EntityPrototype>(prototype.Guid);
        }

        // Looks up an already-authored, shared HitEffectData instance under Resources/HitEffects
        // (BurnEffectData.asset, RiftMarkEffectData.asset, etc. - all zero-config, reading their own
        // magnitudes from RuntimeConfig.EffectConfig) rather than creating a Remix-specific copy.
        private static AssetRef<HitEffectData> LoadHitEffect(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<HitEffectData>($"{HitEffectsFolderPath}/{name}.asset");

            if (asset == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No HitEffectData asset at {HitEffectsFolderPath}/{name}.asset - Remix's pool (or Bass Drop's Stun) is missing an entry.");
                return default;
            }

            return new AssetRef<HitEffectData>(asset.Guid);
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

        private static void WireCharacterData(ResonancePassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Wires the 4 Hero Skill Ascensions into ZaraBaseSkill.Actions - CheckActions stays 0 either
        // way (same "the CheckActions bug" reasoning docs/brute-ascensions.md already documents for
        // Juggernaut), since these execute via SkillSlot.Upgrades once picked. Also sweeps and removes
        // any stray sub-object embedded directly in the asset file that ISN'T the main ZaraBaseSkill
        // asset (or its own embedded ProjectileData/DirectHitData/SpawnAlternatingAreaEffectData/the
        // Damage Beat's own DamageEffectData/ThrownProjectileMovementData, which this generator doesn't
        // touch beyond ConfigureTotemThrow above) - a safety net against the 8 old dead pre-refactor
        // sub-actions (Bigger Totem/Void Pulse/Haste Pulse/Rapid Pulse/Stunning Pulse/Knockback Pulse/
        // Amplified Healing/Amplified Damage) this asset used to carry embedded, plus the old
        // ZaraHealPulse (replaced by the shared ZaraScaledHealPulse top-level asset).
        private static void WireTotemActions(List<SkillActionData> actions)
        {
            var mainAsset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(ZaraBaseSkillPath);

            if (mainAsset == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No ProjectileSkillData asset at {ZaraBaseSkillPath} - Amplifier/Healing Chorus/Double Time/Main Stage were created, but Actions was not wired.");
                return;
            }

            mainAsset.Actions = actions.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssets();

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(ZaraBaseSkillPath);

            foreach (var obj in allObjects)
            {
                if (obj == null || obj == mainAsset)
                    continue;

                if (obj is ProjectileDataAsset || obj is DirectHitData || obj is SpawnAlternatingAreaEffectData
                    || obj is ThrownProjectileMovementData || (obj is DamageEffectData && obj.name == "ZaraVoidPulseDamage"))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(obj);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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
