namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors RiftShardConfig.asset - the one piece of Editor authoring the Rift Shard currency
    // (see docs/global-upgrades.md "Greed") needs that a script can actually produce. Mirrors
    // LuxAscensionAssetGenerator's own ScrapConfig step exactly, including why RuntimeConfig itself
    // isn't wired here: RuntimeConfig's asset refs live on QuantumMenuConfig.asset (a scene/menu
    // config, not a plain AssetObject this generator can safely locate the same way) - see the log
    // below for the manual step. Re-running is safe: an existing asset at the expected path is
    // updated in place, not duplicated.
    public static class RiftShardAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/Economy";
        private const string ConfigAssetPath = FolderPath + "/RiftShardConfig.asset";

        [MenuItem("Tools/RiftRaiders/Generate Rift Shard Assets")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(FolderPath) == false)
            {
                CreateFolderRecursive(FolderPath);
            }

            var existing = AssetDatabase.LoadAssetAtPath<RiftShardConfig>(ConfigAssetPath);
            bool isNew = existing == null;
            RiftShardConfig config = isNew ? ScriptableObject.CreateInstance<RiftShardConfig>() : existing;

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

            LogHelper.Log("RiftShardAssetGenerator", $"RiftShardConfig authored at {ConfigAssetPath}. Still needed by hand: " +
                "(1) a RiftShardOrb EntityPrototype (Transform3D + PhysicsCollider3D on the Player layer + the RiftShard " +
                "component + DestroyAfterTime, same shape as ExpOrb's own prefab); " +
                "(2) assign this RiftShardConfig.asset and that prototype to RuntimeConfig's RiftShardConfig/RiftShardPrototype " +
                "fields wherever ExperienceConfig/ExpOrbPrototype are already assigned (QuantumMenuConfig.asset); " +
                "(3) run Tools/RiftRaiders/Generate Global Upgrade Assets so Greed's own .asset picks up a real RiftShardMultiplier/" +
                "EnemyHealthBonus tuning.");
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
