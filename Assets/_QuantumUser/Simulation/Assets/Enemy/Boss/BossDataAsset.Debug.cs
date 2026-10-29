namespace Quantum
{
    // Debug-only surface for BossDataAsset - same split/reasoning as EnemyDataAsset.Debug.cs.
    public partial class BossDataAsset
    {
        // Simulation can't reach QuantumRunner/SendCommand, so this just raises
        // EnemyDataAssetDebug.OnJumpToBossRequested; CheatMenu subscribes and sends
        // CheatActionKind.JumpToBoss carrying this asset (see CheatSystem.JumpToBoss) - the real
        // encounter (arena, intro, HUD) with this boss's DebugPrototype, whatever the world's own boss is.
        [EditorButton("Jump To This Boss", EditorButtonVisibility.PlayMode)]
        private void DebugJumpToThisBoss()
        {
            EnemyDataAssetDebug.OnJumpToBossRequested?.Invoke(this);
        }
    }
}
