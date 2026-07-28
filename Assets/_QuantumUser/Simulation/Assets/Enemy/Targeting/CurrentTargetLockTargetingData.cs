namespace Quantum
{
    using Photon.Deterministic;

    // Keeps whatever Enemy.Target already is, as long as it still exists - re-evaluating only once
    // the current target is gone, rather than every tick re-picking whoever is nearest. LockDuration
    // is authored for a future timed-reevaluation pass (would need its own runtime timer on the
    // Enemy component, not present yet) - until then this is an indefinite lock.
    public unsafe class CurrentTargetLockTargetingData : EnemyTargetingData
    {
        public FP LockDuration = 3;

        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(self, out var enemy) == false)
                return EntityRef.None;

            if (enemy->Target != EntityRef.None && f.Exists(enemy->Target) == true)
                return enemy->Target;

            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            EnemyMovementUtility.TryFindNearestPlayer(f, position, range, out EntityRef target);
            return target;
        }
    }
}
