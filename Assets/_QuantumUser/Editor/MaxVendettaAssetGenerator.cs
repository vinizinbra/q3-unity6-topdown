namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Max's Vendetta passive + its 4 Vendetta Upgrades (Unbroken Spirit/Settled Score/
    // Blood Debt/Burning Vengeance), then wires both into MaxCharacterData.asset - repointing
    // Passive away from Adrenaline Rush and retiring Adrenaline Rush's own 4 upgrade entries from
    // PassiveUpgrades, per the explicit "Vendetta replaces Adrenaline Rush" decision in
    // docs/max-vendetta-fire-mastery.md. Deliberately split from MaxFireMasteryAssetGenerator.cs
    // (the other 4 slots in the same 8-entry PassiveUpgrades list) so either half can be
    // regenerated independently - see that file's own comment. Mirrors MaxAdrenalineAssetGenerator.
    // cs's own create-or-update/merge-in-place behavior; re-running this is safe for the same
    // reasons that one is. Does NOT delete AdrenalineRushPassiveData/its upgrade .asset files or
    // their C# classes - only stops MaxCharacterData from referencing them; deleting the
    // now-dead-code Adrenaline files is a separate, deliberate cleanup step.
    public static class MaxVendettaAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Max";
        private const string VendettaUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/Vendetta";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/MaxCharacterData.asset";

        // Fixed paths MaxAdrenalineAssetGenerator itself authors its 4 upgrades at - resolved by
        // path (not by type scan) purely to find their Guid and strip them out of PassiveUpgrades
        // below, same "load a known path" idiom every generator here already uses for wiring.
        private static readonly string[] LegacyAdrenalineUpgradeAssetPaths =
        {
            "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/HotBlooded.asset",
            "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/BattleHigh.asset",
            "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/TooAngryToDie.asset",
            "Assets/_QuantumUser/Resources/Passives/Max/PassiveSkillUpgrades/NoTimeToBreathe.asset",
        };

        [MenuItem("Tools/RiftRaiders/Max/Generate Vendetta Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(VendettaUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not
            // UpgradeData - a hero's single base Passive is Inspector-assigned
            // (CharacterData.Passive), never offered as a level-up card, so no DisplayName/Rarity/
            // Description to set here.
            VendettaPassiveData passive = CreateOrUpdate<VendettaPassiveData>($"{PassivesFolderPath}/VendettaPassiveData.asset", asset =>
            {
                asset.BaseHealMultiplier = FP._0_50;
                asset.BaseMarkDuration = 8;
            });

            UnbrokenSpiritPassiveUpgradeData unbrokenSpirit = CreateOrUpdate<UnbrokenSpiritPassiveUpgradeData>($"{VendettaUpgradesFolderPath}/UnbrokenSpirit.asset", asset =>
            {
                asset.DisplayName = "Unbroken Spirit";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Shield damage now also marks your Vendetta target.";
            });

            SettledScorePassiveUpgradeData settledScore = CreateOrUpdate<SettledScorePassiveUpgradeData>($"{VendettaUpgradesFolderPath}/SettledScore.asset", asset =>
            {
                asset.DisplayName = "Settled Score";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Vendetta heals for the full amount of damage dealt during the mark.";
                asset.HealMultiplier = FP._1;
            });

            BloodDebtPassiveUpgradeData bloodDebt = CreateOrUpdate<BloodDebtPassiveUpgradeData>($"{VendettaUpgradesFolderPath}/BloodDebt.asset", asset =>
            {
                asset.DisplayName = "Blood Debt";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Vendetta marks last longer.";
                asset.AdditionalDuration = 4;
            });

            BurningVengeancePassiveUpgradeData burningVengeance = CreateOrUpdate<BurningVengeancePassiveUpgradeData>($"{VendettaUpgradesFolderPath}/BurningVengeance.asset", asset =>
            {
                asset.DisplayName = "Burning Vengeance";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Consuming a Vendetta mark spreads Burn to nearby enemies.";
                asset.Radius = 4;
                asset.BurnDuration = 3;
                asset.BurnIntensity = 5;
                asset.MaxTargets = 4;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { unbrokenSpirit, settledScore, bloodDebt, burningVengeance });

            LogHelper.Log("MaxVendettaAssetGenerator", "Vendetta passive + 4 Vendetta Upgrades authored and wired into MaxCharacterData " +
                      "(Passive repointed from AdrenalineRushPassiveData, its 4 upgrades removed from PassiveUpgrades). " +
                      "Run Generate Fire Mastery Assets too for the remaining 4 PassiveUpgrades slots. " +
                      "AdrenalineRushPassiveData/its upgrade .asset+.cs files were NOT deleted - safe to delete by hand now that nothing references them.");
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

        private static void WireCharacterData(VendettaPassiveData passive, List<PassiveUpgradeData> vendettaUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("MaxVendettaAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                LogHelper.Warn("MaxVendettaAssetGenerator", $"MaxCharacterData.Passive was already set to {characterData.Passive} - overwriting with VendettaPassiveData.");
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);

            var legacyAdrenalineGuids = new HashSet<long>(LegacyAdrenalineUpgradeAssetPaths
                .Select(path => AssetDatabase.LoadAssetAtPath<PassiveUpgradeData>(path))
                .Where(asset => asset != null)
                .Select(asset => asset.Guid.Value));

            characterData.PassiveUpgrades = characterData.PassiveUpgrades
                .Where(assetRef => legacyAdrenalineGuids.Contains(assetRef.Id.Value) == false)
                .ToList();

            foreach (var upgrade in vendettaUpgrades)
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
