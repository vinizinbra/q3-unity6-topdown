namespace Quantum
{
    using System;

    // View-side hook for EnemyDataAsset's "Spawn Near Local Player" debug button - see
    // EnemyDataAsset.Debug.cs. Args: enemy asset, count. CheatMenu (CHEATS_ENABLED builds) is the
    // subscriber; null otherwise, so the button silently no-ops.
    public static class EnemyDataAssetDebug
    {
        public static Action<AssetRef<EnemyDataAsset>, int> OnSpawnRequested;
    }
}
