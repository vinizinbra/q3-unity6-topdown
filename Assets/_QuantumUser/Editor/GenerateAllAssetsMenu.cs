namespace QuantumUser.Editor
{
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
            Debug.Log("[GenerateAllAssetsMenu] Regenerating all RiftRaiders assets...");

            WeaponPerkAssetGenerator.Generate();
            GlobalUpgradeAssetGenerator.Generate();
            KaiVoidFieldAssetGenerator.Generate();
            LuxScrapAssetGenerator.Generate();
            PixieChainReactionAssetGenerator.Generate();
            MaxAdrenalineAssetGenerator.Generate();
            ZaraResonanceAssetGenerator.Generate();
            BruteProtectorAssetGenerator.Generate();

            Debug.Log("[GenerateAllAssetsMenu] Done - see individual generator log lines above for per-asset counts.");
        }
    }
}
