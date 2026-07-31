namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects an ExpOrb once any player walks within pickup range - see ExperienceUtility.
    // TrySpawnDrop for how orbs are spawned. Whichever player actually reaches it determines the
    // radius (their own CharacterStats.PickupRangeMultiplier) AND scales the granted amount by
    // their own CharacterStats.ExperienceGainMultiplier, but the exp itself is credited to the
    // whole co-op run, not that player specifically - see ExperienceUtility.Grant/Experience.qtn.
    // No magnetism/homing today, an orb just sits where it dropped until a player's own collection
    // radius reaches it or DestroyAfterTime expires it.
    [Preserve]
    public unsafe class ExpOrbSystem : SystemMainThreadFilter<ExpOrbSystem.Filter>
    {
        // Comfortably larger than any realistic PickupRangeMultiplier stack so the broadphase
        // query never misses a player who'd otherwise qualify once their own multiplier is
        // applied below - a known simplification, see docs/experience-drops.md.
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                return;

            ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
            FP queryRadius = config.PickupRadius * QueryRadiusScale;

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, filter.Transform3D->Position, queryRadius);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef player = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                    continue;

                FP pickupRadius = config.PickupRadius * stats->PickupRangeMultiplier;
                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > pickupRadius * pickupRadius)
                    continue;

                ExperienceUtility.Grant(f, filter.ExpOrb->Value * stats->ExperienceGainMultiplier);
                f.Events.ExpOrbCollected(player, filter.Transform3D->Position, filter.ExpOrb->Value);
                f.Destroy(filter.Entity);
                return;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ExpOrb* ExpOrb;
            public Transform3D* Transform3D;
        }
    }
}
