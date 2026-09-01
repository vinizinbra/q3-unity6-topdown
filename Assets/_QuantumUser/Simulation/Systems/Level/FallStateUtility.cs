namespace Quantum
{
    using Photon.Deterministic;

    // Single "is this entity currently in a fall-respawn delay window" check (see PlayerFallSystem/
    // EnemyFallSystem/LevelConfig.FallRespawnDelay) - shared by every View component that hides or
    // freezes something for it (BlobAnimationView, WeaponViewController, CharView's camera freeze,
    // EnemyBlobAnimationView, CharacterUiWidget). One prefab/widget often serves both entity types
    // (see CharacterUiWidget's own header comment), so this checks whichever fall-timer component
    // the entity actually carries rather than each call site re-deriving that branch itself.
    public static unsafe class FallStateUtility
    {
        public static bool IsFallPending(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PlayerMovement>(entity, out var movement) == true)
                return movement->FallRespawnTimer > FP._0;

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == true)
                return enemy->FallRespawnTimer > FP._0;

            return false;
        }
    }
}
