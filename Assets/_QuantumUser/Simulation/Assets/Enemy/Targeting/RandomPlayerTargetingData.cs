namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Picks uniformly among every ALIVE player within DetectionRange via f.RNG (deterministic) -
    // never UnityEngine.Random. A Downed/KO player is excluded from the pool entirely (see
    // docs/revive.md) - two passes over the candidates (count eligible, then pick the k-th eligible
    // one) rather than a single candidates[index] lookup, so the uniform pick is still only among
    // players actually worth targeting.
    //
    // NOTE: candidate ORDER changed when this stopped using a physics query (see
    // PlayerQueryUtility), so a given RNG roll no longer resolves to the same player it used to.
    // Same uniform distribution over the same eligible set, still fully deterministic - just not
    // bit-identical to pre-PlayerQueryUtility replays.
    public unsafe class RandomPlayerTargetingData : EnemyTargetingData
    {
        public override EntityRef SelectTarget(Frame f, EntityRef self)
        {
            if (TryGetSelfContext(f, self, out FP range, out FPVector3 position) == false)
                return EntityRef.None;

            Span<EntityRef> candidates = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int candidateCount = EnemyMovementUtility.FindPlayersInRadius(f, position, range, candidates);
            int eligibleCount = 0;

            for (int i = 0; i < candidateCount; i++)
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, candidates[i]) == false)
                    eligibleCount++;
            }

            if (eligibleCount == 0)
                return EntityRef.None;

            int index = f.RNG->Next(0, eligibleCount);

            for (int i = 0; i < candidateCount; i++)
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, candidates[i]) == true)
                    continue;

                if (index == 0)
                    return candidates[i];

                index--;
            }

            return EntityRef.None;
        }
    }
}
