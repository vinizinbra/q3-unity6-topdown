namespace Quantum
{
    using Photon.Deterministic;

    // Targets whichever player (within DetectionRange) has the most OTHER players within
    // ClusterRadius of them - aims into the thick of a group instead of picking off stragglers,
    // the opposite intent to MostIsolatedPlayerTargetingData.
    public unsafe class LargestPlayerClusterTargetingData : EnemyTargetingData
    {
        public FP ClusterRadius = 5;

        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, position, range);
            FP clusterRadiusSqr = ClusterRadius * ClusterRadius;

            EntityRef best = EntityRef.None;
            int bestCount = -1;
            FP bestSqrDistanceToSelf = default;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                // Downed/KO players are neither a valid primary target nor worth counting toward
                // someone else's cluster size - see docs/revive.md.
                if (PlayerLifeStateUtility.IsIncapacitated(f, candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var candidateTransform) == false)
                    continue;

                int clusterCount = 0;

                for (int j = 0; j < hits.Count; j++)
                {
                    if (j == i)
                        continue;

                    if (PlayerLifeStateUtility.IsIncapacitated(f, hits[j].Entity) == true)
                        continue;

                    if (f.Unsafe.TryGetPointer<Transform3D>(hits[j].Entity, out var otherTransform) == false)
                        continue;

                    if (EnemyMovementUtility.FlatSqrDistance(candidateTransform->Position, otherTransform->Position) <= clusterRadiusSqr)
                        clusterCount++;
                }

                FP sqrDistanceToSelf = EnemyMovementUtility.FlatSqrDistance(position, candidateTransform->Position);

                // Ties broken by proximity to self, the same "closest wins" default every other
                // targeting concrete here falls back to.
                bool better = clusterCount > bestCount
                    || (clusterCount == bestCount && (best == EntityRef.None || sqrDistanceToSelf < bestSqrDistanceToSelf));

                if (better == true)
                {
                    best = candidate;
                    bestCount = clusterCount;
                    bestSqrDistanceToSelf = sqrDistanceToSelf;
                }
            }

            return best;
        }
    }
}
