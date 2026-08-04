namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Zara's Resonance passive + its 4 Passive Ascensions + all 3 Dash Ascensions (Quick
    // Tempo, Healing Step, Afterbeat - all fully code-complete, unlike every other hero's remaining
    // dash ascension, which still needs a hand-authored EntityPrototype), then wires all of it into
    // ZaraCharacterData.asset. Mirrors LuxScrapAssetGenerator.cs/KaiVoidFieldAssetGenerator.cs/
    // MaxAdrenalineAssetGenerator.cs/BruteProtectorAssetGenerator.cs/
    // PixieChainReactionAssetGenerator.cs exactly (same folder-creation/update-in-place/rebuild-the-
    // list-from-scratch behavior); re-running this is safe for the same reasons those are.
    public static class ZaraResonanceAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/ZaraCharacterData.asset";
        private const string HitEffectsFolderPath = "Assets/_QuantumUser/Resources/HitEffects";

        [MenuItem("Tools/RiftRaiders/Zara/Generate Resonance Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            ResonancePassiveData passive = CreateOrUpdate<ResonancePassiveData>($"{PassivesFolderPath}/ResonancePassiveData.asset", asset =>
            {
                asset.Max = 100;
                asset.GenerationPerDamage = FP._1;
                asset.Radius = 5;
                asset.HealPercent = FP.FromString("0.1");
                asset.DamageAmount = 15;
                asset.KnockbackTier = KnockbackTier.Small;
            });

            FasterTempoPassiveUpgradeData fasterTempo = CreateOrUpdate<FasterTempoPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/FasterTempo.asset", asset =>
            {
                asset.DisplayName = "Faster Tempo";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Generate Resonance faster.";
                asset.GenerationBonus = FP.FromString("0.25");
            });

            RestorativeBeatPassiveUpgradeData restorativeBeat = CreateOrUpdate<RestorativeBeatPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/RestorativeBeat.asset", asset =>
            {
                asset.DisplayName = "Restorative Beat";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases pulse healing.";
                asset.HealPercentBonus = FP.FromString("0.1");
            });

            HeavyBassPassiveUpgradeData heavyBass = CreateOrUpdate<HeavyBassPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/HeavyBass.asset", asset =>
            {
                asset.DisplayName = "Heavy Bass";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases pulse damage and knockback.";
                asset.DamageBonus = 10;
            });

            RemixPassiveUpgradeData remix = CreateOrUpdate<RemixPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Remix.asset", asset =>
            {
                asset.DisplayName = "Remix";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Every third Resonance Pulse also applies a random effect to enemies caught in it.";

                // All 5 already exist as shared, zero-config HitEffectData instances (they read their
                // own magnitudes from RuntimeConfig.EffectConfig) - reused here rather than authoring
                // Remix-specific variants.
                asset.Effects = new List<AssetRef<HitEffectData>>
                {
                    LoadHitEffect("BurnEffectData"),
                    LoadHitEffect("RiftMarkEffectData"),
                    LoadHitEffect("SlowEffectData"),
                    LoadHitEffect("StunEffectData"),
                };
            });

            QuickTempoSkillAction quickTempo = CreateOrUpdate<QuickTempoSkillAction>($"{DashUpgradesFolderPath}/QuickTempoSkillAction.asset", asset =>
            {
                asset.DisplayName = "Quick Tempo";
                asset.Rarity = UpgradeRarity.Rare;
                asset.ResonanceOnDash = 20;
            });

            HealingStepSkillAction healingStep = CreateOrUpdate<HealingStepSkillAction>($"{DashUpgradesFolderPath}/HealingStepSkillAction.asset", asset =>
            {
                asset.DisplayName = "Healing Step";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Radius = 5;
                asset.HealPercent = FP.FromString("0.1");
            });

            AfterbeatSkillAction afterbeat = CreateOrUpdate<AfterbeatSkillAction>($"{DashUpgradesFolderPath}/AfterbeatSkillAction.asset", asset =>
            {
                asset.DisplayName = "Afterbeat";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Delay = FP._1;
                asset.Damage = 20;
                asset.Radius = 4;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { fasterTempo, restorativeBeat, heavyBass, remix },
                new List<SkillActionData> { quickTempo, healingStep, afterbeat });

            LogHelper.Log("ZaraResonanceAssetGenerator", "Passive + 4 ascensions + all 3 dash ascensions authored and wired into " +
                      "ZaraCharacterData - unlike every other hero this pass, nothing further needs manual EntityPrototype authoring.");
        }

        // Looks up an already-authored, shared HitEffectData instance under Resources/HitEffects
        // (BurnEffectData.asset, RiftMarkEffectData.asset, etc. - all zero-config, reading their own
        // magnitudes from RuntimeConfig.EffectConfig) rather than creating a Remix-specific copy.
        private static AssetRef<HitEffectData> LoadHitEffect(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<HitEffectData>($"{HitEffectsFolderPath}/{name}.asset");

            if (asset == null)
            {
                LogHelper.Error("ZaraResonanceAssetGenerator", $"No HitEffectData asset at {HitEffectsFolderPath}/{name}.asset - Remix's pool is missing an entry.");
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
                LogHelper.Error("ZaraResonanceAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                LogHelper.Warn("ZaraResonanceAssetGenerator", $"ZaraCharacterData.Passive was already set to {characterData.Passive} - overwriting with ResonancePassiveData.");
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();

            foreach (var upgrade in dashUpgrades)
            {
                bool alreadyPresent = characterData.DashSkillUpgrades.Any(existing => existing.Id.Value == upgrade.Guid.Value);

                if (alreadyPresent == true)
                    continue;

                characterData.DashSkillUpgrades.Add(new AssetRef<SkillActionData>(upgrade.Guid));
            }

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
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
