namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Brute's 4 Knockback Mastery Hero Traits (Ground Pound/Crushing Blow/Lasting Impact/
    // Overwhelming Force) and appends them into BruteCharacterData.PassiveUpgrades - the other 4
    // slots alongside BruteProtectorAssetGenerator's own 4 Passive Ascensions, same "deliberately a
    // separate generator/menu item so either half can be re-run independently" reasoning
    // MaxFireMasteryAssetGenerator.cs/PixieDemolitionMasteryAssetGenerator.cs already use relative to
    // their own base-passive generators.
    //
    // Critical difference from BruteProtectorAssetGenerator's own WireCharacterData: that one fully
    // REPLACES PassiveUpgrades (it's the sole owner of the base-passive wiring). This generator only
    // ever ADDS to that list - append-if-missing, the exact dedup pattern
    // MaxAdrenalineAssetGenerator's own DashSkillUpgrades loop already uses - so running this never
    // deletes the 4 existing Protector Aura entries.
    public static class BruteKnockbackMasteryAssetGenerator
    {
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Brute/PassiveSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/BruteCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Brute/Generate Knockback Mastery Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassiveUpgradesFolderPath);

            GroundPoundPassiveUpgradeData groundPound = CreateOrUpdate<GroundPoundPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/GroundPound.asset", asset =>
            {
                asset.DisplayName = "Ground Pound";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Landing from a fall knocks back nearby enemies.";
                asset.Radius = 4;
                asset.Tier = KnockbackTier.Medium;
                asset.MinFallDistance = 2;
            });

            CrushingBlowPassiveUpgradeData crushingBlow = CreateOrUpdate<CrushingBlowPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/CrushingBlow.asset", asset =>
            {
                asset.DisplayName = "Crushing Blow";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Deal bonus damage to Stunned enemies.";
                asset.DamageMultiplierBonus = FP.FromString("0.4");
            });

            LastingImpactPassiveUpgradeData lastingImpact = CreateOrUpdate<LastingImpactPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/LastingImpact.asset", asset =>
            {
                asset.DisplayName = "Lasting Impact";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Enemies you Stun stay Stunned longer.";
                asset.DurationMultiplierBonus = FP._0_50;
            });

            OverwhelmingForcePassiveUpgradeData overwhelmingForce = CreateOrUpdate<OverwhelmingForcePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/OverwhelmingForce.asset", asset =>
            {
                asset.DisplayName = "Overwhelming Force";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Increases your knockback force.";
                asset.KnockbackMultiplierBonus = FP.FromString("0.3");
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(new List<PassiveUpgradeData> { groundPound, crushingBlow, lastingImpact, overwhelmingForce });

            LogHelper.Log("BruteKnockbackMasteryAssetGenerator", "4 Knockback Mastery traits authored and appended to BruteCharacterData.PassiveUpgrades " +
                      "(existing Protector Aura entries left untouched).");
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

        private static void WireCharacterData(List<PassiveUpgradeData> knockbackMasteryUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("BruteKnockbackMasteryAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            foreach (var upgrade in knockbackMasteryUpgrades)
            {
                bool alreadyPresent = characterData.PassiveUpgrades.Any(existing => existing.Id.Value == upgrade.Guid.Value);

                if (alreadyPresent == true)
                    continue;

                characterData.PassiveUpgrades.Add(new AssetRef<PassiveUpgradeData>(upgrade.Guid));
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
