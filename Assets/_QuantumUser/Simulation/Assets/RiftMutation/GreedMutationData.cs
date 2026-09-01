namespace Quantum
{
    using Photon.Deterministic;

    // Team economy versus tougher enemies. RUN-SCOPE: both halves change shared state, so it applies
    // exactly once no matter how many players are offered it (see MutationScope/RunMutations.qtn).
    //
    // The reward is run-wide precisely because the drawback is - every enemy in the match gains
    // health the moment anyone takes this, so everyone paying that price shares the payout. That's
    // why the shard bonus goes to Frame.Global (applied by RiftShardUtility.GrantAll to the base
    // amount, before each player's own multiplier) rather than only to the picker's own
    // CharacterStats.RiftShardGainMultiplier, which is what it used to do.
    public unsafe class GreedMutationData : RiftMutationData
    {
        public FP RiftShardGainBonus = FP._0;
        public FP EnemyHealthBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.Global->RiftShardGainBonus += RiftShardGainBonus;
            f.Global->EnemyMaxHealthBonus += EnemyHealthBonus;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            RiftShardGainBonus.AsFloat * 100f,
            EnemyHealthBonus.AsFloat * 100f
        };
    }
}
