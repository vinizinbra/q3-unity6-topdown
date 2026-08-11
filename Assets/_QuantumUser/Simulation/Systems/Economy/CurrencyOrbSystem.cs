namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a CurrencyOrb once any player walks within pickup range - replaces what used to be
    // three near-identical systems (ExpOrbSystem/CoinOrbSystem/RiftShardOrbSystem), dispatching on
    // CurrencyOrb.Type for whichever Config/CharacterStats gain multiplier/grant method/View event
    // differs per currency. NOTE: CoinOrbSystem/RiftShardOrbSystem were never actually registered
    // in SystemSetup.User.cs before this merge (only ExpOrbSystem was) - so this also fixes that
    // gap, Coin/RiftShard collection now actually runs.
    //
    // Whichever player actually reaches an orb determines the radius (their own CharacterStats.
    // PickupRangeMultiplier) AND scales the granted amount by their own gain multiplier, but the
    // total itself is credited to the whole co-op run, not that player specifically - see
    // ExperienceUtility/CoinUtility/RiftShardUtility.Grant, all of which write to a shared
    // Frame.Global field. No magnetism/homing today, an orb just sits where it dropped until a
    // player's own collection radius reaches it or DestroyAfterTime expires it.
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
                Grant(f, filter.CurrencyOrb->Type, value * ResolveGainMultiplier(stats, filter.CurrencyOrb->Type));
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

        private static FP ResolveGainMultiplier(CharacterStats* stats, CurrencyOrbType type)
        {
            switch (type)
            {
                case CurrencyOrbType.Experience: return stats->ExperienceGainMultiplier;
                case CurrencyOrbType.Coin: return stats->CoinGainMultiplier;
                case CurrencyOrbType.RiftShard: return stats->RiftShardGainMultiplier;
                default: return FP._1;
            }
        }

        // Deliberately NOT unified into one shared Grant signature - ExperienceUtility.Grant does
        // real extra work (level curve evaluation, LevelUpUtility.BeginLevelUpScreen) that Coin's/
        // RiftShard's own Grant (a plain += with no side effects) don't share, so each keeps its
        // own method and this just dispatches to the right one.
        private static void Grant(Frame f, CurrencyOrbType type, FP amount)
        {
            switch (type)
            {
                case CurrencyOrbType.Experience: ExperienceUtility.Grant(f, amount); break;
                case CurrencyOrbType.Coin: CoinUtility.Grant(f, amount); break;
                case CurrencyOrbType.RiftShard: RiftShardUtility.Grant(f, amount); break;
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
