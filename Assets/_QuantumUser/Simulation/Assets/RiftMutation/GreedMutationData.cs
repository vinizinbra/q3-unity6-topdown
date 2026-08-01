namespace Quantum
{
    using Photon.Deterministic;

    // Doubles Rift Shard gain (CharacterStats.RiftShardGainMultiplier) and raises every enemy's Max
    // Health for the rest of the run (Frame.Global.EnemyHealthBonusMultiplier, read by
    // EnemySystem.SeedHealth) - the enemy-scaling half is run-wide, not per-entity, so it affects
    // every enemy the instant any player picks this, co-op or not. See docs/rift-mutations.md.
    public unsafe class GreedMutationData : RiftMutationData
    {
        public FP RiftShardMultiplier = FP._1;
        public FP EnemyHealthBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->RiftShardGainMultiplier = FPMath.Max(FP._0, stats->RiftShardGainMultiplier * RiftShardMultiplier);
            }

            f.Global->EnemyHealthBonusMultiplier += EnemyHealthBonus;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (RiftShardMultiplier.AsFloat - 1f) * 100f,
            FPMath.RoundToInt(EnemyHealthBonus * 100)
        };
    }
}
