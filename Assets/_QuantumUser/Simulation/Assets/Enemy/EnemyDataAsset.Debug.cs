namespace Quantum
{
    // Debug-only surface for EnemyDataAsset - same split/reasoning as WeaponDataAsset.Debug.cs
    // (Quantum's EditorButtonAttribute, since this asmdef can't see NaughtyAttributes' [Button]).
    public partial class EnemyDataAsset
    {
        // Simulation can't reach QuantumRunner/SendCommand, so this just raises
        // EnemyDataAssetDebug.OnSpawnRequested; CheatMenu subscribes and sends
        // CheatActionKind.SpawnEnemy for the local player (see CheatSystem.SpawnEnemy).
        [EditorButton("Spawn Near Local Player", EditorButtonVisibility.PlayMode)]
        private void DebugSpawnNearLocalPlayer()
        {
            EnemyDataAssetDebug.OnSpawnRequested?.Invoke(this, 1);
        }
    }
}
