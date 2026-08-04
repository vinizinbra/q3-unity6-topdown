namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Generic capped-radius enemy query - every existing area utility (HitEffectUtility.
    // ApplyExplosion/ApplyDamageInRadius, WeaponPerkUtility.TryFindNearestEnemy) either has no
    // target cap or only returns the single nearest match, so a "hit up to N enemies in a radius"
    // mechanic (Max's Wildfire/Burning Vengeance Burn spread, Flashpoint's capped explosion) has
    // nowhere to go. No hero awareness at all - see docs/max-vendetta-fire-mastery.md.
    public static unsafe class AreaQueryUtility
    {
        // Same Enemy/Dead/Invulnerable filtering shape WeaponPerkUtility.TryFindNearestEnemy already
        // uses, just capped at maxTargets instead of narrowed to the single closest. Overlap order
        // (not distance-sorted) is fine here - every caller today treats every caught enemy
        // identically (Burn applied/damage dealt), with no "closest first" requirement.
        public static List<EntityRef> FindEnemiesInRadius(Frame f, FPVector3 center, FP radius, EntityRef exclude, int maxTargets)
        {
            var results = new List<EntityRef>(maxTargets > 0 ? maxTargets : 0);

            if (radius <= FP._0 || maxTargets <= 0)
                return results;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count && results.Count < maxTargets; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (candidate == exclude || f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == false)
                    continue;

                if (enemy->Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(candidate) == true)
                    continue;

                results.Add(candidate);
            }

            return results;
        }
    }
}
