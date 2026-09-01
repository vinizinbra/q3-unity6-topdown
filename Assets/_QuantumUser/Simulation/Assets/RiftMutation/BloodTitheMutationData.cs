namespace Quantum
{
    using Photon.Deterministic;

    // The whole team is paid more, and the whole team is in more danger for it. RUN-SCOPE.
    //
    // Renamed from "Blood Money" to free that name for a personal, Coin-based Legendary of the same
    // flavour - this one is the TEAM-wide version and always was, so the tithe reading (everyone
    // pays, everyone collects) is the more accurate name anyway.
    //
    // Deliberately a different axis from Greed: that one pays for its economy with enemy HEALTH
    // (fights take longer), this one pays with enemy DAMAGE (fights are riskier). Both are run-wide
    // and both are data-driven, so the two can be tuned independently or stacked into a genuinely
    // punishing run.
    //
    // Unlike the health bonus, the damage bonus is read LIVE per hit
    // (HitEffectUtility.ScaleByEnemyDamageMultiplier), so it applies to enemies already on screen
    // the instant it is picked rather than only to newly-spawned ones.
    public unsafe class BloodTitheMutationData : RiftMutationData
    {
        public FP RiftShardGainBonus = FP._0;
        public FP EnemyDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.Global->RiftShardGainBonus += RiftShardGainBonus;
            f.Global->EnemyDamageBonus += EnemyDamageBonus;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            RiftShardGainBonus.AsFloat * 100f,
            EnemyDamageBonus.AsFloat * 100f
        };
    }
}
