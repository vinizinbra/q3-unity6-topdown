namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Max's 4 Fire Mastery Hero Traits (Hot Target/Cremation/Wildfire/Flashpoint) and
    // appends them into MaxCharacterData.PassiveUpgrades - the other 4 of the 8 total entries,
    // alongside MaxVendettaAssetGenerator's own 4 Vendetta Upgrades. Deliberately a separate
    // generator/menu item from that one: Fire Mastery is independent of Vendetta itself (none of
    // these 4 traits touch RevengeConfig/RevengeMark - see docs/max-vendetta-fire-mastery.md),
    // and keeping the two generators independent lets either be re-run/regenerated on its own
    // without touching the other's assets or CharacterData.Passive. Only ever adds to
    // PassiveUpgrades (never removes/reorders), so running this before or after Generate Vendetta
    // Assets gives the same end result. Mirrors MaxAdrenalineAssetGenerator.cs's own create-or-
    // update-in-place behavior; re-running this is safe for the same reasons that one is.
    public static class MaxFireMasteryAssetGenerator
    {
        private const string FireMasteryUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/FireMastery";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/MaxCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Max/Generate Fire Mastery Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(FireMasteryUpgradesFolderPath);

            HotTargetPassiveUpgradeData hotTarget = CreateOrUpdate<HotTargetPassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/HotTarget.asset", asset =>
            {
                asset.DisplayName = "Hot Target";
                asset.Rarity = UpgradeRarity.Common;
                asset.Description = "Increased Critical Chance against Burning enemies.";
                asset.CriticalChanceBonusVsBurning = FP._0_10;
            });

            CremationPassiveUpgradeData cremation = CreateOrUpdate<CremationPassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/Cremation.asset", asset =>
            {
                asset.DisplayName = "Cremation";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Executes Burning enemies whose Health is already below a threshold.";
                asset.NormalHealthThreshold = FP.FromString("0.15");
                asset.EliteHealthThreshold = FP._0_10;
                asset.BossHealthThreshold = FP._0_05;
                asset.BossExecutionEnabled = false;
            });

            WildfirePassiveUpgradeData wildfire = CreateOrUpdate<WildfirePassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/Wildfire.asset", asset =>
            {
                asset.DisplayName = "Wildfire";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Killing a Burning enemy spreads the fire to nearby enemies.";
                asset.Radius = 4;
                asset.BurnDuration = 3;
                asset.BurnIntensity = 5;
                asset.MaxTargets = 4;
            });

            FlashpointPassiveUpgradeData flashpoint = CreateOrUpdate<FlashpointPassiveUpgradeData>($"{FireMasteryUpgradesFolderPath}/Flashpoint.asset", asset =>
            {
                asset.DisplayName = "Flashpoint";
                asset.Rarity = UpgradeRarity.Legendary;
                asset.Description = "Critical hits against Burning enemies detonate a fiery explosion.";
                asset.Radius = 3;
                asset.DamageCoefficient = FP._0_50;
                asset.ProcCooldown = 2;
                asset.MaxTargets = 5;
                asset.AllowRecursiveProc = false;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(new List<PassiveUpgradeData> { hotTarget, cremation, wildfire, flashpoint });

            LogHelper.Log("MaxFireMasteryAssetGenerator", "4 Fire Mastery Hero Traits authored and appended to MaxCharacterData.PassiveUpgrades. " +
                      "Run Generate Vendetta Assets too (either order) for the remaining 4 PassiveUpgrades slots + Passive itself.");
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

        private static void WireCharacterData(List<PassiveUpgradeData> fireMasteryTraits)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("MaxFireMasteryAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            foreach (var trait in fireMasteryTraits)
            {
                bool alreadyPresent = characterData.PassiveUpgrades.Any(existing => existing.Id.Value == trait.Guid.Value);

                if (alreadyPresent == true)
                    continue;

                characterData.PassiveUpgrades.Add(new AssetRef<PassiveUpgradeData>(trait.Guid));
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
