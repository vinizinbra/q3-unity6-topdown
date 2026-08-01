namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors CoinConfig.asset - the one piece of Editor authoring the Coin currency (a second,
    // independent currency from Rift Shards - see docs/global-upgrades.md "Economy") needs that a
    // script can actually produce. Mirrors RiftShardAssetGenerator.cs exactly, including why
    // RuntimeConfig itself isn't wired here: RuntimeConfig's asset refs live on
    // QuantumMenuConfig.asset (a scene/menu config, not a plain AssetObject this generator can
    // safely locate the same way) - see the log below for the manual step. Re-running is safe: an
    // existing asset at the expected path is updated in place, not duplicated.
    public static class CoinAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/Economy";
        private const string ConfigAssetPath = FolderPath + "/CoinConfig.asset";

        [MenuItem("Tools/RiftRaiders/Generate Coin Assets")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(FolderPath) == false)
            {
                CreateFolderRecursive(FolderPath);
            }

            var existing = AssetDatabase.LoadAssetAtPath<CoinConfig>(ConfigAssetPath);
            bool isNew = existing == null;
            CoinConfig config = isNew ? ScriptableObject.CreateInstance<CoinConfig>() : existing;

            config.PickupRadius = 1;
            config.OrbLifetime = 30;
            config.MinSpawnOffset = FP._0;
            config.MaxSpawnOffset = FP._1_50;

            if (isNew)
            {
                AssetDatabase.CreateAsset(config, ConfigAssetPath);
            }
            else
            {
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            LogHelper.Log("CoinAssetGenerator", $"CoinConfig authored at {ConfigAssetPath}. Still needed by hand: " +
                "(1) a CoinOrb EntityPrototype (Transform3D + PhysicsCollider3D on the Player layer + the Coin " +
                "component + DestroyAfterTime, same shape as ExpOrb/RiftShardOrb's own prefab); " +
                "(2) assign this CoinConfig.asset and that prototype to RuntimeConfig's CoinConfig/CoinPrototype " +
                "fields wherever ExperienceConfig/ExpOrbPrototype are already assigned (QuantumMenuConfig.asset); " +
                "(3) tune CoinValue/CoinDropChance per tier on EnemyTierStatsConfig.asset - both default to 1 " +
                "(always drops 1 Coin) until authored otherwise.");
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
