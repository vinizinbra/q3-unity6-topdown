namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Sweeps every leftover CurrencyOrb (XP/Coin/Rift Shard) to its nearest player once an area is
    // secured, spread over a fixed window rather than all landing on the same tick - Begin/Tick are
    // both called from SurvivalProgressionUtility.Tick (Begin once on the Global.BreathingAreaSecured
    // false -> true edge, Tick every tick unconditionally, same shape as PhaseTimer/SurvivalTime's own
    // per-tick advance right above it in that file). Each orb is still collected through the exact
    // same CurrencyOrbSystem.Collect endpoint its own normal walk-into-range pickup uses
    // (Grant/RaiseCollectedEvent/OnCollectibleCollected/f.Destroy) - this only decides WHEN and WHO,
    // never re-implements the crediting itself.
    //
    // Nearest player is picked purely to have a collector to attribute the pickup event to - it has
    // no gameplay meaning: Coin/Rift Shard broadcast to every connected player's own wallet and
    // Experience is one shared run total (see CurrencyOrbSystem.Grant), so who "collects" a swept
    // orb doesn't change who gets credited.
    public static unsafe class CurrencyOrbVacuumUtility
    {
        // Decisive placeholder, not a balance knob (see feedback on giving decisive placeholder
        // numbers) - however many orbs are left over, ALL of them finish landing within this same
        // window, only the per-tick rate adapts.
        private static readonly FP Duration = FP._1;

        public static void Begin(Frame f)
        {
            f.Global->OrbVacuumTimeRemaining = Duration;
        }

        public static void Tick(Frame f)
        {
            FP remaining = f.Global->OrbVacuumTimeRemaining;

            if (remaining <= FP._0)
                return;

            FP newRemaining = FPMath.Max(remaining - f.DeltaTime, FP._0);
            f.Global->OrbVacuumTimeRemaining = newRemaining;

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int playerCount = PlayerQueryUtility.GatherPlayers(f, players);

            if (playerCount == 0)
                return;

            var filtered = f.Filter<CurrencyOrb, Transform3D>();
            List<EntityRef> orbs = null;

            while (filtered.Next(out EntityRef entity, out CurrencyOrb _, out Transform3D _) == true)
            {
                orbs ??= new List<EntityRef>();
                orbs.Add(entity);
            }

            if (orbs == null)
                return;

            // Window fully elapsed - whatever's still left (frame-rate rounding, or simply the last
            // few orbs) gets collected right now rather than left behind. Otherwise, peel off just
            // enough of what's CURRENTLY left that, at this tick's rate, every orb still finishes
            // within the time still remaining (evaluated BEFORE this tick's own decrement above, so
            // the very last tick's tiny remaining-time naturally forces count == orbs.Count).
            int collectCount = newRemaining <= FP._0
                ? orbs.Count
                : Math.Clamp(FPMath.CeilToInt(orbs.Count * (f.DeltaTime / remaining)), 1, orbs.Count);

            for (int i = 0; i < collectCount; i++)
            {
                CollectNearest(f, orbs[i], players, playerCount);
            }
        }

        private static void CollectNearest(Frame f, EntityRef orb, Span<EntityRef> players, int playerCount)
        {
            if (f.Unsafe.TryGetPointer<CurrencyOrb>(orb, out var currencyOrb) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == false)
                return;

            EntityRef nearest = EntityRef.None;
            CharacterStats* nearestStats = null;
            FP nearestSqrDistance = default;

            for (int i = 0; i < playerCount; i++)
            {
                if (f.Unsafe.TryGetPointer<Transform3D>(players[i], out var playerTransform) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<CharacterStats>(players[i], out var stats) == false)
                    continue;

                FP sqrDistance = (playerTransform->Position - orbTransform->Position).SqrMagnitude;

                if (nearest != EntityRef.None && sqrDistance >= nearestSqrDistance)
                    continue;

                nearest = players[i];
                nearestStats = stats;
                nearestSqrDistance = sqrDistance;
            }

            if (nearest == EntityRef.None)
                return;

            CurrencyOrbSystem.Collect(f, orb, currencyOrb, orbTransform->Position, nearest, nearestStats);
        }
    }
}
