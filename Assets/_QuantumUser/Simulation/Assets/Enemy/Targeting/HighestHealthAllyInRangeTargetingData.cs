namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Targets whichever OTHER enemy (ally) within DetectionRange has the highest MaxHealth (not
    // current health) - for a Shielder-type enemy that wants to protect the tankiest ally
    // regardless of how much damage that ally has already taken.
    public unsafe class HighestHealthAllyInRangeTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            Shape3D sphere = Shape3D.CreateSphere(range);
            var hits = f.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphere, EnemyMovementUtility.GetEnemyLayerMask(f), QueryOptions.HitAll);

            EntityRef best = EntityRef.None;
            FP bestMaxHealth = default;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (candidate == self)
                    continue;

                if (f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == true && enemy->Phase == EnemyActionPhase.Dead)
                    continue;

                if (f.Unsafe.TryGetPointer<Health>(candidate, out var health) == false)
                    continue;

                if (best == EntityRef.None || health->MaxHealth > bestMaxHealth)
                {
                    best = candidate;
                    bestMaxHealth = health->MaxHealth;
                }
            }

            return best;
        }
    }
}
