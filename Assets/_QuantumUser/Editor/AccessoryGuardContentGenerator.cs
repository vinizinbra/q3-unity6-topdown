namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors AccessoryGuardConfig.asset with the prototype values from docs/accessory-guard.md -
    // MaxDurability 3, repair 25/50, replacement 100. Mirrors ReviveContentGenerator's own
    // folder-creation/update-in-place shape; re-running this is safe, an existing asset at the
    // expected path is updated rather than duplicated.
    //
    // Deliberately does NOT touch RuntimeConfig/QuantumMenuConfig, the DroppedAccessory prototype,
    // any hero CharacterData's Accessory presentation block, or any UI prefab wiring - the same
    // "no safe way to locate the right asset from a script" reason every other generator here has.
    // See the log message below (and the doc's own checklist) for exactly what's left by hand.
    public static class AccessoryGuardContentGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/Accessory";
        private const string ConfigPath = FolderPath + "/AccessoryGuardConfig.asset";

        [MenuItem("Tools/RiftRaiders/Generate Accessory Guard Content")]
        internal static void Generate()
        {
            CreateFolderRecursive(FolderPath);

            var config = LoadOrCreate<AccessoryGuardConfig>(ConfigPath, out bool isNew);

            config.BaseDurability = 3;

            // Placeholder pending a real balance pass: a Filler/Swarm tap (typically 1-2 damage)
            // should pass straight through to Health instead of costing a durability point, while a
            // Normal-tier-or-above hit still gets blocked.
            config.MinDamageToBlock = 3;

            config.MinDropOffset = FP._1;
            config.MaxDropOffset = 3;
            config.LandingSampleAttempts = 8;
            config.MinLaunchAngle = 40;
            config.MaxLaunchAngle = 65;

            config.PickupRadius = FP._1 + FP._0_25;

            // Prototype pricing (docs/accessory-guard.md): one point missing is cheap, two is
            // noticeably worse, a total loss is worst. Explicit per-step costs, no formula.
            config.RepairCostByMissingDurability = new[] { (FP)25, (FP)50 };
            config.BrokenReplacementCost = 100;

            FinalizeAsset(config, ConfigPath, isNew);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogHelper.Log("AccessoryGuardContentGenerator",
                $"AccessoryGuardConfig ({ConfigPath}) authored. Still needed by hand: " +
                "(1) assign RuntimeConfig's AccessoryGuardConfig field (QuantumMenuConfig.asset), same " +
                "place ReviveConfig/CursedRiftConfig etc. are already assigned - until then the whole " +
                "mechanic stays off (nothing is seeded, nothing blocks); " +
                "(2) build ONE shared DroppedAccessory EntityPrototype and assign it to " +
                "RuntimeConfig.Prefabs.DroppedAccessoryPrototype - easiest is to duplicate " +
                "Entities/Prefabs/CoinOrb.prefab and swap QPrototypeCurrencyOrb for " +
                "QPrototypeDroppedAccessory, then add a DroppedAccessoryView to the sprite child. It " +
                "needs Transform3D + GroundOffset + DroppedAccessory; GroundOffset is NOT optional, " +
                "PopMotionSystem filters on it and without it the accessory never lands. One prototype " +
                "serves every hero - the sprite is swapped per owner at spawn; " +
                "(3) per hero, author CharacterData.Accessory (DisplayName / CollectibleSprite) on each " +
                "hero's own CharacterData asset - MaxGeometricHat.png is already imported for Max; " +
                "(4) per hero, add an AccessoryView component to that hero's View prefab and assign its " +
                "equippedVisual/unequippedVisual - the two hand-placed GameObjects it switches between " +
                "(e.g. head_0 wearing the cap vs. head_0 without it). Both optional: assigning only " +
                "equippedVisual degrades to a plain single toggle; " +
                "(5) raise ChooseWindow.cardCount from 3 to 4 on the scene's choiceWindows[] instance - " +
                "the Merchant's Accessory Repair/Replacement card is appended at food-card index 3 and " +
                "has no widget to render into below 4; " +
                "(6) nothing else: durability, blocking, dropping, recovery and repair pricing are all " +
                "hero-agnostic and driven entirely by the asset this generator just wrote.");
        }

        private static T LoadOrCreate<T>(string path, out bool isNew) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            isNew = existing == null;
            return isNew ? ScriptableObject.CreateInstance<T>() : existing;
        }

        private static void FinalizeAsset(AssetObject asset, string path, bool isNew)
        {
            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath) == true)
                return;

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
