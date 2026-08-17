namespace QuantumUser.Editor
{
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // One button that runs every "Generate ... Assets" menu command in this folder back to back -
    // each generator is idempotent (creates missing assets, updates existing ones by FileName/path)
    // and already logs its own created/updated/wired summary, so this just chains them and adds a
    // start/end marker. Keep this list in sync whenever a new AssetGenerator with a
    // [MenuItem("Tools/RiftRaiders/.../Generate ... Assets")] is added.
    public static class GenerateAllAssetsMenu
    {
        [MenuItem("Tools/RiftRaiders/Generate All Assets")]
        private static void GenerateAll()
        {
            LogHelper.Log("GenerateAllAssetsMenu", "Regenerating all RiftRaiders assets...");

            WeaponPerkAssetGenerator.Generate();
            GlobalUpgradeAssetGenerator.Generate();
            KaiAscensionAssetGenerator.Generate();
            LuxScrapAssetGenerator.Generate();
            PixieAscensionAssetGenerator.Generate();
            MaxAscensionAssetGenerator.Generate();
            ZaraAscensionAssetGenerator.Generate();
            BruteAscensionAssetGenerator.Generate();

            LogHelper.Log("GenerateAllAssetsMenu", "Done - see individual generator log lines above for per-asset counts.");
        }
    }
}
