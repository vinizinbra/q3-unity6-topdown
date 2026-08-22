namespace Quantum
{
    using System;
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
        // Same shape CurrencyOrbSystem uses - the padded broadphase prefilter it used to share is
        // gone (see that system's own comment); the authoritative per-player center test below is
        // unchanged.
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.RuntimeConfig.HealthOrbConfig.IsValid == false)
                return;

            FP pickupRadius = f.FindAsset(f.RuntimeConfig.HealthOrbConfig).PickupRadius;

            if (pickupRadius <= FP._0)
                return;

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int playerCount = PlayerQueryUtility.GatherPlayers(f, players);

            EntityRef collector = EntityRef.None;
            Health* collectorHealth = null;
            FP closestSqrDistance = default;

            for (int i = 0; i < playerCount; i++)
            {
                EntityRef player = players[i];

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

                // Nearest wins - see CurrencyOrbSystem's own note on this.
                if (collector != EntityRef.None && sqrDistance >= closestSqrDistance)
                    continue;

                collector = player;
                collectorHealth = health;
                closestSqrDistance = sqrDistance;
            }

            if (collector == EntityRef.None)
                return;

            // Percentage of the collector's own MaxHealth (see HealthOrb.qtn). Owner == the healed
            // player itself, so HealUtility's own HealingReceivedMultiplier (read off owner's
            // CharacterStats) applies exactly as it would for any self-received heal.
            HealUtility.ApplyFlatHeal(f, collector, collector, collectorHealth,
                collectorHealth->MaxHealth * filter.HealthOrb->HealPercent);
            f.Destroy(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public HealthOrb* HealthOrb;
            public Transform3D* Transform3D;
        }
    }
}
