namespace Quantum
{
    using Photon.Deterministic;

    // Reproduces the original (pre-modular) EnemySystem target-acquisition behavior exactly, once
    // paired with EnemySystem's own decoy-priority check (see EnemyTargetingData's class comment).
    public unsafe class NearestPlayerTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            EnemyMovementUtility.TryFindNearestPlayer(f, position, range, out EntityRef target);
            return target;
        }
    }
}
