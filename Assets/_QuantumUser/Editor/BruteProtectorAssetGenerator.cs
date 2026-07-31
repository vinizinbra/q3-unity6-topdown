namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using UnityEditor;
    using UnityEngine;

    // Authors Brute's Protector passive + its 4 Passive Ascensions + 2 of his 3 Dash Ascensions
    // (Iron Shoulder, Bodyguard - Barricade still needs a collider-only EntityPrototype authored by
    // hand first, see the log this prints), then wires all of it into BruteCharacterData.asset.
    // Mirrors LuxScrapAssetGenerator.cs/KaiVoidFieldAssetGenerator.cs/MaxAdrenalineAssetGenerator.cs
    // exactly (same folder-creation/update-in-place/rebuild-the-list-from-scratch behavior);
    // re-running this is safe for the same reasons those are.
    public static class BruteProtectorAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Brute";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Brute/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Brute/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/BruteCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Brute/Generate Protector Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here.
            ProtectorPassiveData passive = CreateOrUpdate<ProtectorPassiveData>($"{PassivesFolderPath}/ProtectorPassiveData.asset", asset =>
            {
                asset.Radius = 6;
                asset.IntimidateDamageMultiplier = FP.FromString("0.75");
            });

            BulwarkPassiveUpgradeData bulwark = CreateOrUpdate<BulwarkPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Bulwark.asset", asset =>
            {
                asset.DisplayName = "Bulwark";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases the Protector Aura's radius.";
                asset.RadiusBonus = 3;
            });

            GuardianPassiveUpgradeData guardian = CreateOrUpdate<GuardianPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Guardian.asset", asset =>
            {
                asset.DisplayName = "Guardian";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Allies inside the aura gain Damage Reduction.";
                asset.AllyDamageReductionAmount = FP.FromString("0.25");
            });

            IronPresencePassiveUpgradeData ironPresence = CreateOrUpdate<IronPresencePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/IronPresence.asset", asset =>
            {
                asset.DisplayName = "Iron Presence";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Intimidated enemies move slower and have reduced knockback resistance.";
                asset.SlowMultiplier = FP.FromString("0.75");
                asset.KnockbackTakenMultiplier = FP._1_50;
            });

            FearlessPassiveUpgradeData fearless = CreateOrUpdate<FearlessPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Fearless.asset", asset =>
            {
                asset.DisplayName = "Fearless";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Brute deals increased damage to Intimidated enemies.";
                asset.BonusVsIntimidated = FP.FromString("0.25");
            });

            IronShoulderSkillAction ironShoulder = CreateOrUpdate<IronShoulderSkillAction>($"{DashUpgradesFolderPath}/IronShoulderSkillAction.asset", asset =>
            {
                asset.DisplayName = "Iron Shoulder";
                asset.Rarity = UpgradeRarity.Rare;
                asset.KnockbackTier = KnockbackTier.Strong;
                asset.WallCheckDistance = 2;
                asset.StunDuration = 1;
            });

            BodyguardSkillAction bodyguard = CreateOrUpdate<BodyguardSkillAction>($"{DashUpgradesFolderPath}/BodyguardSkillAction.asset", asset =>
            {
                asset.DisplayName = "Bodyguard";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Radius = 6;
                asset.ShieldRestoreFraction = FP.FromString("0.2");
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { bulwark, guardian, ironPresence, fearless },
                new List<SkillActionData> { ironShoulder, bodyguard });

            Debug.Log("[BruteProtectorAssetGenerator] Passive + 4 ascensions + 2 dash ascensions authored and wired into BruteCharacterData. " +
                      "Barricade (the 3rd dash ascension) still needs a collider-only EntityPrototype authored by hand first " +
                      "(a wall prefab with just Transform3D + PhysicsCollider3D + DestroyAfterTime - no AreaDamage/Decoy, so it " +
                      "just sits there and blocks, same as any other SpawnEntitySkillAction prototype) - once that exists, add a " +
                      "SpawnEntitySkillAction asset pointing Prototype at it (Phase=End, Anchor=Caster) and append it to " +
                      "BruteCharacterData.DashSkillUpgrades the same way this script just did for the other two.");
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

        private static void WireCharacterData(ProtectorPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                Debug.LogError($"[BruteProtectorAssetGenerator] No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                Debug.LogWarning($"[BruteProtectorAssetGenerator] BruteCharacterData.Passive was already set to {characterData.Passive} - overwriting with ProtectorPassiveData.");
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
