namespace Quantum
{
    using Photon.Deterministic;

    // Spends a flat, data-driven amount of the sacrificing player's OWN Rift Shard wallet
    // (per-player since the currency conversion - see docs/breathing-poi.md/
    // CharacterStats.RiftShards) - flat rather than percent-based (unlike Coin Offering), since
    // Rift Shards are meant to be a scarcer, more deliberate spend (e.g. "5 -> 2").
    public unsafe class RiftShardOfferingSacrificeData : SacrificeDefinition
    {
        public FP ShardCost = 3;

        public override bool IsEligible(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true && stats->RiftShards >= ShardCost;
        }

        public override void ApplyCost(Frame f, EntityRef entity)
        {
            RiftShardUtility.TrySpend(f, entity, ShardCost);
        }

        public override string BuildValuePreview(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return string.Empty;

            FP after = stats->RiftShards - ShardCost;

            return $"SHARDS\n{stats->RiftShards.AsFloat:0} -> {after.AsFloat:0}";
        }
    }
}
