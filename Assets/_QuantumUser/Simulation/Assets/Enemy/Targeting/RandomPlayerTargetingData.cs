namespace Quantum
{
    using Photon.Deterministic;

    // Picks uniformly among every player within DetectionRange via f.RNG (deterministic) - never
    // UnityEngine.Random.
    public unsafe class RandomPlayerTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, position, range);

            if (hits.Count == 0)
                return EntityRef.None;

            int index = f.RNG->Next(0, hits.Count);
            return hits[index].Entity;
        }
    }
}
