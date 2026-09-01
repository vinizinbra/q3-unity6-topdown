namespace Quantum
{
    using Photon.Deterministic;

    // Base for every target-selection policy; each subclass is its own Quantum asset, the same
    // "one asset per behavior" shape as AttackData/EnemyMovementData. Shared/reused across enemies
    // via AssetRef<EnemyTargetingData> - SelectTarget must stay a pure function of (Frame,
    // EntityRef), no mutable fields written at runtime.
    //
    // Deliberately does NOT own decoy priority ("max aggro") - that's an orthogonal override
    // EnemySystem applies on top of whichever targeting policy is active (see UpdateIdle/
    // UpdateChasing), not something baked into one specific profile.
    public abstract unsafe partial class EnemyTargetingData : AssetObject
    {
        public abstract EntityRef SelectTarget(Frame f, EntityRef self);

        // Shared resolution every concrete targeting type needs: where "self" is, and how far it
        // can perceive - both live on the enemy's own EnemyDataAsset, not on this targeting asset
        // (shared, reused across many enemies with different ranges).
        protected static bool TryGetSelfContext(Frame f, EntityRef self, out FP detectionRange, out FPVector3 position)
        {
            detectionRange = default;
            position = default;

            if (f.Unsafe.TryGetPointer<Enemy>(self, out var enemy) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            detectionRange = data.AI.ResolveDetectionRange();
            position = transform->Position;
            return true;
        }
    }
}
