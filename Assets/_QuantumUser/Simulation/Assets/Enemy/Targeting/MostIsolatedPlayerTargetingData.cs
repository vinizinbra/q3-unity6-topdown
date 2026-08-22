namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Targets whichever player (within DetectionRange) is farthest from their own nearest other
    // player - the loneliest target, for enemies that want to punish players who split up.
    public unsafe class MostIsolatedPlayerTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            Span<EntityRef> candidates = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int candidateCount = EnemyMovementUtility.FindPlayersInRadius(f, position, range, candidates);

            EntityRef best = EntityRef.None;
            FP bestIsolationSqr = default;

            for (int i = 0; i < candidateCount; i++)
            {
                EntityRef candidate = candidates[i];

                // Downed/KO players are neither a valid primary target nor worth counting as
                // "another nearby player" for someone else's isolation score - see docs/revive.md.
                if (PlayerLifeStateUtility.IsIncapacitated(f, candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var candidateTransform) == false)
                    continue;

                bool hasOther = false;
                FP nearestOtherSqr = default;

                for (int j = 0; j < candidateCount; j++)
                {
                    if (j == i)
                        continue;

                    if (PlayerLifeStateUtility.IsIncapacitated(f, candidates[j]) == true)
                        continue;

                    if (f.Unsafe.TryGetPointer<Transform3D>(candidates[j], out var otherTransform) == false)
                        continue;

                    FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(candidateTransform->Position, otherTransform->Position);

                    if (hasOther == false || sqrDistance < nearestOtherSqr)
                    {
                        hasOther = true;
                        nearestOtherSqr = sqrDistance;
                    }
                }

                // No other player at all within range - maximally isolated, takes it immediately.
                if (hasOther == false)
                    return candidate;

                if (best == EntityRef.None || nearestOtherSqr > bestIsolationSqr)
                {
                    best = candidate;
                    bestIsolationSqr = nearestOtherSqr;
                }
            }

            return best;
        }
    }
}
