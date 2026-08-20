namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a HealthOrb (see HealthOrb.qtn) once any player walks within pickup range - same
    // walk-into-radius auto-collect + broadphase-then-per-player-radius shape as CurrencyOrbSystem,
    // but heals the collecting player's own Health (HealUtility.ApplyFlatHeal) instead of crediting a
    // run-wide currency total. Whichever player reaches it determines the radius (their own
    // CharacterStats.PickupRangeMultiplier) and receives the heal (capped at their MaxHealth by
    // ApplyFlatHeal). No magnetism - the orb sits where it dropped until reached or DestroyAfterTime
    // expires it. Registered inside GameplaySystemGroup alongside CurrencyOrbSystem.
    [Preserve]
    public unsafe class HealthOrbSystem : SystemMainThreadFilter<HealthOrbSystem.Filter>
    {
        // Same broadphase margin CurrencyOrbSystem uses - comfortably larger than any realistic
        // PickupRangeMultiplier stack so the query never misses a player who'd qualify once their own
        // multiplier is applied below.
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.RuntimeConfig.HealthOrbConfig.IsValid == false)
                return;

            FP pickupRadius = f.FindAsset(f.RuntimeConfig.HealthOrbConfig).PickupRadius;

            if (pickupRadius <= FP._0)
                return;

            FP queryRadius = pickupRadius * QueryRadiusScale;
            var hits = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(f, filter.Transform3D->Position, queryRadius);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef player = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Health>(player, out var health) == false)
                    continue;

                FP effectiveRadius = pickupRadius * stats->PickupRangeMultiplier;
                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > effectiveRadius * effectiveRadius)
                    continue;

                // Percentage of the collector's own MaxHealth (see HealthOrb.qtn). Owner == the healed
                // player itself, so HealUtility's own HealingReceivedMultiplier (read off owner's
                // CharacterStats) applies exactly as it would for any self-received heal.
                HealUtility.ApplyFlatHeal(f, player, player, health, health->MaxHealth * filter.HealthOrb->HealPercent);
                f.Destroy(filter.Entity);
                return;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public HealthOrb* HealthOrb;
            public Transform3D* Transform3D;
        }
    }
}
