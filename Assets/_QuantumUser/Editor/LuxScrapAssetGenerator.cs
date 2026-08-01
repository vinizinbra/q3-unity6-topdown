namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Lux's Scrap Collector passive + its 4 Passive Ascensions + 2 of her 3 Dash Ascensions
    // (Emergency Repair, Portable Cover - Decoy Beacon still needs a Decoy EntityPrototype authored
    // by hand first, see the log this prints), then wires all of it into LuxCharacterData.asset -
    // the two pieces of Editor authoring the design doc's own "still needed" checklist calls out.
    // Mirrors GlobalUpgradeAssetGenerator.cs exactly (same folder-creation/update-in-place/rebuild-
    // the-list-from-scratch behavior); re-running this is safe for the same reasons that one is.
    public static class LuxScrapAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Lux";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Lux/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Lux/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/LuxCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Lux/Generate Scrap Collector Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // Not wired into RuntimeConfig here - unlike LevelUpConfig/WeaponPerkPoolData, RuntimeConfig's
            // asset refs live on QuantumMenuConfig.asset (a scene/menu config, not a plain AssetObject
            // this generator can safely locate the same way) - see the log below for the manual step.
            CreateOrUpdate<ScrapConfig>($"{PassivesFolderPath}/ScrapConfig.asset", asset =>
            {
                asset.PickupRadius = 2;
                asset.OrbLifetime = 30;
                asset.MinSpawnOffset = FP._0_50;
                asset.MaxSpawnOffset = FP._1_50;
            });

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            // The real payoff is the 10-stack free Hero Skill charge (see ScrapUtility.Grant) - the
            // flat per-pickup cooldown shave lives on the Rapid Recycling ascension instead now.
            ScrapCollectorPassiveData passive = CreateOrUpdate<ScrapCollectorPassiveData>($"{PassivesFolderPath}/ScrapCollectorPassiveData.asset", asset =>
            {
                asset.DropChance = FP.FromString("0.25");
                asset.StacksRequired = 10;
            });

            EfficientSalvagePassiveUpgradeData efficientSalvage = CreateOrUpdate<EfficientSalvagePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/EfficientSalvage.asset", asset =>
            {
                asset.DisplayName = "Efficient Salvage";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases Scrap drop chance.";
                asset.DropChanceBonus = FP.FromString("0.25");
            });

            EnhacementPassiveUpgradeData enhacement = CreateOrUpdate<EnhacementPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Enhacement.asset", asset =>
            {
                asset.DisplayName = "Enhacement";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Scrap also increases your machine's max health.";
                asset.MachineHealthBonusPerPickup = 5;
            });

            RapidRecyclingPassiveUpgradeData rapidRecycling = CreateOrUpdate<RapidRecyclingPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/RapidRecycling.asset", asset =>
            {
                asset.DisplayName = "Rapid Recycling";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Each Scrap pickup also reduces your Hero Skill's cooldown.";
                asset.CooldownReductionPerPickup = FP._1;
            });

            ScavengerPassiveUpgradeData scavenger = CreateOrUpdate<ScavengerPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Scavenger.asset", asset =>
            {
                asset.DisplayName = "Scavenger";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Filler-tier enemies can also drop Scrap.";
            });

            RepairNearbyMachinesSkillAction emergencyRepair = CreateOrUpdate<RepairNearbyMachinesSkillAction>($"{DashUpgradesFolderPath}/EmergencyRepairSkillAction.asset", asset =>
            {
                asset.DisplayName = "Emergency Repair";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Radius = 6;
                asset.RepairFraction = FP._0_50;
            });

            PortableCoverSkillAction portableCover = CreateOrUpdate<PortableCoverSkillAction>($"{DashUpgradesFolderPath}/PortableCoverSkillAction.asset", asset =>
            {
                asset.DisplayName = "Portable Cover";
                asset.Rarity = UpgradeRarity.Epic;
                asset.ShieldRestoreAmount = 20;
                asset.MachineShieldRestoreAmount = 10;
                asset.MachineRadius = 6;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { efficientSalvage, enhacement, rapidRecycling, scavenger },
                new List<SkillActionData> { emergencyRepair, portableCover });

            LogHelper.Log("LuxScrapAssetGenerator", "Passive + 4 ascensions + 2 dash ascensions authored and wired into LuxCharacterData. " +
                      "Still needed by hand: (1) assign the new ScrapConfig.asset and a ScrapOrb EntityPrototype (a pickup prefab " +
                      "carrying the ScrapOrb component, same shape as ExpOrb's own prefab) to RuntimeConfig's ScrapConfig/ScrapOrbPrototype " +
                      "fields wherever ExperienceConfig/ExpOrbPrototype are already assigned (QuantumMenuConfig.asset); " +
                      "(2) Decoy Beacon (the 3rd dash ascension) needs a Decoy EntityPrototype authored first (Transform3D + " +
                      "PhysicsCollider3D on the Player layer + the Decoy component + DestroyAfterTime, see Decoy.qtn) - once that exists, " +
                      "add a SpawnEntitySkillAction asset pointing Prototype at it (Phase=Begin, Anchor=Caster) and append it to " +
                      "LuxCharacterData.DashSkillUpgrades the same way this script just did for the other two.");
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

        private static void WireCharacterData(ScrapCollectorPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("LuxScrapAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                LogHelper.Warn("LuxScrapAssetGenerator", $"LuxCharacterData.Passive was already set to {characterData.Passive} - overwriting with ScrapCollectorPassiveData.");
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
