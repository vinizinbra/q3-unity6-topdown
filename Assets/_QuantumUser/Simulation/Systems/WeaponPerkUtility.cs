namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared helpers for weapon-perk post-impact/reaction effects (Ricochet, Quantum Rounds,
    // Critical Rebound) - the "find another enemy near a point" query every one of them needs,
    // mirroring AreaHitData.FindNearbyEnemies's own overlap-and-filter shape, just narrowed to the
    // single nearest match instead of collecting every one found.
    public static unsafe class WeaponPerkUtility
    {
        public static bool TryFindNearestEnemy(Frame f, FPVector3 center, FP radius, EntityRef exclude, out EntityRef result)
        {
            result = EntityRef.None;

            if (radius <= FP._0)
                return false;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            FP closestSqrDistance = FP.MaxValue;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (candidate == exclude || f.Has<Enemy>(candidate) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                    continue;

                FP sqrDistance = (transform->Position - center).SqrMagnitude;

                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                result = candidate;
            }

            return result != EntityRef.None;
        }
    }
}
