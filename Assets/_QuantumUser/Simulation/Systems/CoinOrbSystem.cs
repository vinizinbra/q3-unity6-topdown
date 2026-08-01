namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a Coin once any player walks within pickup range - see CoinUtility.TrySpawnDrop for
    // how coins are spawned. Mirrors RiftShardOrbSystem/ExpOrbSystem exactly: whichever player
    // actually reaches it determines the radius (their own CharacterStats.PickupRangeMultiplier)
    // AND scales the granted amount by their own CharacterStats.CoinGainMultiplier, but the coin
    // itself is credited to the whole co-op run, not that player specifically - see
    // CoinUtility.Grant/Coins.qtn.
    [Preserve]
    public unsafe class CoinOrbSystem : SystemMainThreadFilter<CoinOrbSystem.Filter>
    {
        // Same broadphase-safety margin as ExpOrbSystem.QueryRadiusScale/RiftShardOrbSystem's own -
        // see that field's own comment.
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.RuntimeConfig.CoinConfig.IsValid == false)
                return;

            CoinConfig config = f.FindAsset(f.RuntimeConfig.CoinConfig);
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

                CoinUtility.Grant(f, filter.Coin->Value * stats->CoinGainMultiplier);
                f.Events.CoinCollected(player, filter.Transform3D->Position, filter.Coin->Value);
                f.Destroy(filter.Entity);
                return;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Coin* Coin;
            public Transform3D* Transform3D;
        }
    }
}
