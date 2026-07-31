namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using UnityEditor;
    using UnityEngine;

    // Authors Pixie's Chain Reaction passive + its 4 Passive Ascensions + 2 of her 3 Dash Ascensions
    // (Backblast, Volatile Escape - Bombs Away still needs a standalone (non-projectile) timed-bomb
    // EntityPrototype authored by hand first, see the log this prints), then wires all of it into
    // PixieCharacterData.asset. Mirrors LuxScrapAssetGenerator.cs/KaiVoidFieldAssetGenerator.cs/
    // MaxAdrenalineAssetGenerator.cs/BruteProtectorAssetGenerator.cs exactly (same folder-creation/
    // update-in-place/rebuild-the-list-from-scratch behavior); re-running this is safe for the same
    // reasons those are.
    public static class PixieChainReactionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Pixie";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Pixie/PassiveSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Pixie/DashSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/PixieCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Pixie/Generate Chain Reaction Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);

            // PassiveData (unlike PassiveUpgradeData) derives AssetObject directly, not UpgradeData -
            // a hero's single base Passive is Inspector-assigned (CharacterData.Passive), never
            // offered as a level-up card, so it has no DisplayName/Rarity/Description to set here,
            // and it has no tunable fields of its own - see ChainReactionPassiveData's own comment.
            ChainReactionPassiveData passive = CreateOrUpdate<ChainReactionPassiveData>($"{PassivesFolderPath}/ChainReactionPassiveData.asset", _ => { });

            BiggerBoomPassiveUpgradeData biggerBoom = CreateOrUpdate<BiggerBoomPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/BiggerBoom.asset", asset =>
            {
                asset.DisplayName = "Bigger Boom";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases explosion radius.";
                asset.RadiusMultiplierBonus = FP.FromString("0.25");
            });

            UnstableMixturePassiveUpgradeData unstableMixture = CreateOrUpdate<UnstableMixturePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/UnstableMixture.asset", asset =>
            {
                asset.DisplayName = "Unstable Mixture";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Increases explosion damage.";
                asset.DamageMultiplierBonus = FP.FromString("0.25");
            });

            ExplosiveRoundsPassiveUpgradeData explosiveRounds = CreateOrUpdate<ExplosiveRoundsPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/ExplosiveRounds.asset", asset =>
            {
                asset.DisplayName = "Explosive Rounds";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Every shot also detonates a small explosion.";
                asset.Radius = 2;
                asset.DamageMultiplier = FP._0_50;
            });

            HeavyPayloadPassiveUpgradeData heavyPayload = CreateOrUpdate<HeavyPayloadPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/HeavyPayload.asset", asset =>
            {
                asset.DisplayName = "Heavy Payload";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Specialist and stronger enemies create much larger explosions.";
                asset.ExplosionMultiplier = FP._2;
            });

            BackblastSkillAction backblast = CreateOrUpdate<BackblastSkillAction>($"{DashUpgradesFolderPath}/BackblastSkillAction.asset", asset =>
            {
                asset.DisplayName = "Backblast";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Radius = 4;
                asset.Damage = 20;
            });

            VolatileEscapeSkillAction volatileEscape = CreateOrUpdate<VolatileEscapeSkillAction>($"{DashUpgradesFolderPath}/VolatileEscapeSkillAction.asset", asset =>
            {
                asset.DisplayName = "Volatile Escape";
                asset.Rarity = UpgradeRarity.Rare;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive, new List<PassiveUpgradeData> { biggerBoom, unstableMixture, explosiveRounds, heavyPayload },
                new List<SkillActionData> { backblast, volatileEscape });

            Debug.Log("[PixieChainReactionAssetGenerator] Passive + 4 ascensions + 2 dash ascensions authored and wired into PixieCharacterData. " +
                      "Bombs Away (the 3rd dash ascension) still needs a standalone timed-bomb EntityPrototype authored by hand first - " +
                      "NOT the existing BunnyBombEntityPrototype (that one is wired for ProjectileSpawner.Spawn's own launch/velocity setup, " +
                      "not SpawnedEntitySpawner.Spawn's simpler create-and-place path, so reusing it directly risks an uninitialized " +
                      "Projectile). Author a new prototype with the same AreaHitData-driven fuse/detonate behavior instead, then add a " +
                      "SpawnEntitySkillAction asset pointing Prototype at it (Phase=End, Anchor=Caster) and append it to " +
                      "PixieCharacterData.DashSkillUpgrades the same way this script just did for the other two.");
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

        private static void WireCharacterData(ChainReactionPassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                Debug.LogError($"[PixieChainReactionAssetGenerator] No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            if (characterData.Passive.IsValid == true && characterData.Passive.Id.Value != passive.Guid.Value)
            {
                Debug.LogWarning($"[PixieChainReactionAssetGenerator] PixieCharacterData.Passive was already set to {characterData.Passive} - overwriting with ChainReactionPassiveData.");
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
