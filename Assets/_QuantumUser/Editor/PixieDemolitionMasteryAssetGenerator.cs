namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Pixie's 4 Demolition Mastery Hero Traits (Direct Hit/Concussive Force/Volatile Payload/
    // Mini Ordnance) and appends them into PixieCharacterData.PassiveUpgrades - the other 4 slots
    // alongside PixieChainReactionAssetGenerator's own 4 Passive Ascensions, same "deliberately a
    // separate generator/menu item so either half can be re-run independently" reasoning
    // MaxFireMasteryAssetGenerator.cs already uses relative to MaxVendettaAssetGenerator.cs.
    //
    // Critical difference from PixieChainReactionAssetGenerator's own WireCharacterData: that one
    // fully REPLACES PassiveUpgrades (it's the sole owner of the base-passive wiring). This
    // generator only ever ADDS to that list - append-if-missing, the exact dedup pattern
    // MaxAdrenalineAssetGenerator's own DashSkillUpgrades loop already uses - so running this never
    // deletes the 4 existing Chain Reaction entries.
    public static class PixieDemolitionMasteryAssetGenerator
    {
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Pixie/PassiveSkillUpgrades";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/PixieCharacterData.asset";

        [MenuItem("Tools/RiftRaiders/Pixie/Generate Demolition Mastery Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassiveUpgradesFolderPath);

            DirectHitPassiveUpgradeData directHit = CreateOrUpdate<DirectHitPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/DirectHit.asset", asset =>
            {
                asset.DisplayName = "Direct Hit";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Enemies near the center of your explosions take increased damage.";
                asset.InnerRadiusFraction = FP.FromString("0.35");
                asset.DamageMultiplierBonus = FP._0_50;
            });

            ConcussiveForcePassiveUpgradeData concussiveForce = CreateOrUpdate<ConcussiveForcePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/ConcussiveForce.asset", asset =>
            {
                asset.DisplayName = "Concussive Force";
                asset.Rarity = UpgradeRarity.Rare;
                asset.Description = "Enemies near the center of your explosions are knocked back.";
                asset.InnerRadiusFraction = FP._0_50;
                asset.Force = 8;
                asset.UpwardForce = 2;
                asset.EliteMultiplier = FP.FromString("0.4");
            });

            VolatilePayloadPassiveUpgradeData volatilePayload = CreateOrUpdate<VolatilePayloadPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/VolatilePayload.asset", asset =>
            {
                asset.DisplayName = "Volatile Payload";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Critical explosion hits apply Burn.";
                asset.BurnDuration = 3;
                asset.BurnIntensity = 5;
            });

            MiniOrdnancePassiveUpgradeData miniOrdnance = CreateOrUpdate<MiniOrdnancePassiveUpgradeData>($"{PassiveUpgradesFolderPath}/MiniOrdnance.asset", asset =>
            {
                asset.DisplayName = "Cluster Charges";
                asset.Rarity = UpgradeRarity.Epic;
                asset.Description = "Your explosions have a chance to leave behind a Mini Bomb that explodes after a short delay.";
                asset.Chance = FP.FromString("0.25");
                asset.Damage = 10;
                asset.Fuse = FP.FromString("0.4");
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(new List<PassiveUpgradeData> { directHit, concussiveForce, volatilePayload, miniOrdnance });

            LogHelper.Log("PixieDemolitionMasteryAssetGenerator", "4 Demolition Mastery traits authored and appended to PixieCharacterData.PassiveUpgrades " +
                      "(existing Chain Reaction entries left untouched). MiniOrdnance.MiniBombPrototype/Explosion still need assigning by hand - " +
                      "a minimal stationary EntityPrototype (Transform3D only, no PhysicsCollider3D/movement data - see ExplodeOnDestroy.qtn's own " +
                      "comment) and an AreaHitData asset with a small BlastRadius, neither of which this generator can author.");
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

        private static void WireCharacterData(List<PassiveUpgradeData> demolitionMasteryUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("PixieDemolitionMasteryAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            foreach (var upgrade in demolitionMasteryUpgrades)
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
