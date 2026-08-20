namespace Quantum
{
    using Photon.Deterministic;

    // Picks uniformly among every ALIVE player within DetectionRange via f.RNG (deterministic) -
    // never UnityEngine.Random. A Downed/KO player is excluded from the pool entirely (see
    // docs/revive.md) - two passes over hits (count eligible, then pick the k-th eligible one)
    // rather than a single hits[index] lookup, so the uniform pick is still only among players
    // actually worth targeting.
    public unsafe class RandomPlayerTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, position, range);
            int eligibleCount = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, hits[i].Entity) == false)
                    eligibleCount++;
            }

            if (eligibleCount == 0)
                return EntityRef.None;

            int index = f.RNG->Next(0, eligibleCount);

            for (int i = 0; i < hits.Count; i++)
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, hits[i].Entity) == true)
                    continue;

                if (index == 0)
                    return hits[i].Entity;

                index--;
            }

            return EntityRef.None;
        }
    }
}
