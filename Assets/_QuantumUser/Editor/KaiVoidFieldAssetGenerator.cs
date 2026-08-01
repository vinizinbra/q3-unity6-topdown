namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Kai's Void Field passive + its 3 Passive Ascensions + 2 of his 3 Dash Ascensions
    // (Reflect, Shockwave - SlowArea still needs a ProjectileSlowField-carrying EntityPrototype
    // authored by hand first, see the log this prints), then wires all of it into
    // KaiCharacterData.asset. Mirrors LuxScrapAssetGenerator.cs/GlobalUpgradeAssetGenerator.cs
    // exactly (same folder-creation/update-in-place/rebuild-the-list-from-scratch behavior);
    // re-running this is safe for the same reasons those are.
    public static class KaiVoidFieldAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Kai";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Kai/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Kai/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/KaiCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Kai/Generate Void Field Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            VoidFieldPassiveData passive = CreateOrUpdate<VoidFieldPassiveData>($"{PassivesFolderPath}/VoidFieldPassiveData.asset", asset =>
            {
                asset.Radius = 4;
                asset.SpeedMultiplier = FP.FromString("0.5");
            });

            EventHorizonPassiveUpgradeData eventHorizon = CreateOrUpdate<EventHorizonPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/EventHorizon.asset", asset =>
            {
                asset.DisplayName = "Event Horizon";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases the Void Field's radius.";
                asset.RadiusBonus = 2;
            });

            TimeDilationPassiveUpgradeData timeDilation = CreateOrUpdate<TimeDilationPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/TimeDilation.asset", asset =>
            {
                asset.DisplayName = "Time Dilation";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases the Void Field's projectile slow.";
                asset.SlowBonus = FP.FromString("0.25");
            });

            VoidPressurePassiveUpgradeData voidPressure = CreateOrUpdate<VoidPressurePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/VoidPressure.asset", asset =>
            {
                asset.DisplayName = "Void Pressure";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Enemies in the Void Field (Filler/Normal/Specialist) have their attacks slowed.";
                asset.EnemyTimeDilationMultiplier = FP.FromString("0.5");
            });

            ReflectProjectilesSkillAction reflect = CreateOrUpdate<ReflectProjectilesSkillAction>($"{DashUpgradesFolderPath}/ReflectProjectilesSkillAction.asset", asset =>
            {
                asset.DisplayName = "Reflect";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Radius = 3;
            });

            DashShockwaveSkillAction shockwave = CreateOrUpdate<DashShockwaveSkillAction>($"{DashUpgradesFolderPath}/DashShockwaveSkillAction.asset", asset =>
            {
                asset.DisplayName = "Shockwave";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Radius = 5;
                asset.Tier = KnockbackTier.Medium;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { eventHorizon, timeDilation, voidPressure },
                new List<SkillActionData> { reflect, shockwave });

            LogHelper.Log("KaiVoidFieldAssetGenerator", "Passive + 3 ascensions + 2 dash ascensions authored and wired into KaiCharacterData. " +
                      "SlowArea (the 3rd dash ascension) still needs a ProjectileSlowField EntityPrototype authored by hand first " +
                      "(just a Transform3D + the ProjectileSlowField component + DestroyAfterTime - no physics collider needed, " +
                      "VoidFieldSystem reads it by component presence, not by overlap) - once that exists, add a " +
                      "SpawnEntitySkillAction asset pointing Prototype at it (Phase=End, Anchor=Caster) and append it to " +
                      "KaiCharacterData.DashSkillUpgrades the same way this script just did for the other two.");
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

        private static void WireCharacterData(VoidFieldPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("KaiVoidFieldAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                LogHelper.Warn("KaiVoidFieldAssetGenerator", $"KaiCharacterData.Passive was already set to {characterData.Passive} - overwriting with VoidFieldPassiveData.");
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
