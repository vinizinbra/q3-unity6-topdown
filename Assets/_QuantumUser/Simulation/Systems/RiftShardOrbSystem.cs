namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a RiftShard once any player walks within pickup range - see RiftShardUtility.
    // TrySpawnDrop for how shards are spawned. Mirrors ExpOrbSystem exactly: whichever player
    // actually reaches it determines the radius (their own CharacterStats.PickupRangeMultiplier)
    // AND scales the granted amount by their own CharacterStats.RiftShardGainMultiplier (doubled by
    // Greed), but the shard itself is credited to the whole co-op run, not that player specifically
    // - see RiftShardUtility.Grant/RiftShards.qtn.
    [Preserve]
    public unsafe class RiftShardOrbSystem : SystemMainThreadFilter<RiftShardOrbSystem.Filter>
    {
        // Same broadphase-safety margin as ExpOrbSystem.QueryRadiusScale - see that field's own
        // comment.
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.RuntimeConfig.RiftShardConfig.IsValid == false)
                return;

            RiftShardConfig config = f.FindAsset(f.RuntimeConfig.RiftShardConfig);
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

                RiftShardUtility.Grant(f, filter.RiftShard->Value * stats->RiftShardGainMultiplier);
                f.Events.RiftShardCollected(player, filter.Transform3D->Position, filter.RiftShard->Value);
                f.Destroy(filter.Entity);
                return;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public RiftShard* RiftShard;
            public Transform3D* Transform3D;
        }
    }
}
