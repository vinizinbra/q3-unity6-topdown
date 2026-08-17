namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a CurrencyOrb once any player walks within pickup range - replaces what used to be
    // three near-identical systems (ExpOrbSystem/CoinOrbSystem/RiftShardOrbSystem), dispatching on
    // CurrencyOrb.Type for whichever Config/grant method/View event differs per currency. NOTE:
    // CoinOrbSystem/RiftShardOrbSystem were never actually registered in SystemSetup.User.cs
    // before this merge (only ExpOrbSystem was) - so this also fixes that gap, Coin/RiftShard
    // collection now actually runs.
    //
    // Whichever player actually reaches an orb determines the radius (their own CharacterStats.
    // PickupRangeMultiplier) and triggers the pickup (destroy + fly/flash event to that one
    // collector), but the actual grant - for Coin/RiftShard - broadcasts to EVERY connected
    // player's own wallet, each scaled by THEIR OWN gain multiplier (CoinUtility.GrantAll/
    // RiftShardUtility.GrantAll - see docs/breathing-poi.md; Experience stays a single shared
    // Frame.Global total, unaffected). No magnetism/homing today, an orb just sits where it
    // dropped until a player's own collection radius reaches it or DestroyAfterTime expires it.
    [Preserve]
    public unsafe class CurrencyOrbSystem : SystemMainThreadFilter<CurrencyOrbSystem.Filter>
    {
        // Comfortably larger than any realistic PickupRangeMultiplier stack so the broadphase
        // query never misses a player who'd otherwise qualify once their own multiplier is
        // applied below - a known simplification, see docs/experience-drops.md.
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            FP pickupRadius = ResolvePickupRadius(f, filter.CurrencyOrb->Type);

            if (pickupRadius <= FP._0)
                return;

            FP queryRadius = pickupRadius * QueryRadiusScale;
            var hits = EnemyMovementUtility.FindPlayersInRadiusForPickup(f, filter.Transform3D->Position, queryRadius);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef player = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                    continue;

                FP effectiveRadius = pickupRadius * stats->PickupRangeMultiplier;
                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > effectiveRadius * effectiveRadius)
                    continue;

                FP value = filter.CurrencyOrb->Value;
                Grant(f, filter.CurrencyOrb->Type, stats, value);
                RaiseCollectedEvent(f, filter.CurrencyOrb->Type, player, filter.Transform3D->Position, value);
                f.Destroy(filter.Entity);
                return;
            }
        }

        private static FP ResolvePickupRadius(Frame f, CurrencyOrbType type)
        {
            switch (type)
            {
                case CurrencyOrbType.Experience:
                    if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                        return FP._0;
                    return f.FindAsset(f.RuntimeConfig.ExperienceConfig).PickupRadius;

                case CurrencyOrbType.Coin:
                    if (f.RuntimeConfig.CoinConfig.IsValid == false)
                        return FP._0;
                    return f.FindAsset(f.RuntimeConfig.CoinConfig).PickupRadius;

                case CurrencyOrbType.RiftShard:
                    if (f.RuntimeConfig.RiftShardConfig.IsValid == false)
                        return FP._0;
                    return f.FindAsset(f.RuntimeConfig.RiftShardConfig).PickupRadius;

                default:
                    return FP._0;
            }
        }

        // Deliberately NOT unified into one shared Grant signature - Experience stays a single
        // shared Frame.Global total credited only for the finder, scaled by THEIR OWN
        // ExperienceGainMultiplier (unchanged from before); Coin/RiftShard now broadcast to every
        // connected player's own wallet via GrantAll, which applies each recipient's OWN gain
        // multiplier individually - so `amount` passed to GrantAll is deliberately the raw,
        // unscaled orb value, not pre-multiplied by the finder's stats.
        private static void Grant(Frame f, CurrencyOrbType type, CharacterStats* finderStats, FP amount)
        {
            switch (type)
            {
                case CurrencyOrbType.Experience: ExperienceUtility.Grant(f, amount * finderStats->ExperienceGainMultiplier); break;
                case CurrencyOrbType.Coin: CoinUtility.GrantAll(f, amount); break;
                case CurrencyOrbType.RiftShard: RiftShardUtility.GrantAll(f, amount); break;
            }
        }

        // Kept as 3 separate events (ExpOrbCollected/CoinCollected/RiftShardCollected) rather than
        // one merged event with a Type field - View code (HitFeedback, FlyingCurrencyManager,
        // ExpBarUiWidget) already subscribes to ExpOrbCollected specifically, so keeping the
        // existing events unchanged means zero View-side changes from this merge.
        private static void RaiseCollectedEvent(Frame f, CurrencyOrbType type, EntityRef collector, FPVector3 position, FP amount)
        {
            switch (type)
            {
                case CurrencyOrbType.Experience: f.Events.ExpOrbCollected(collector, position, amount); break;
                case CurrencyOrbType.Coin: f.Events.CoinCollected(collector, position, amount); break;
                case CurrencyOrbType.RiftShard: f.Events.RiftShardCollected(collector, position, amount); break;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public CurrencyOrb* CurrencyOrb;
            public Transform3D* Transform3D;
        }
    }
}
