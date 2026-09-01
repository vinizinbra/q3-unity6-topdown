namespace Quantum
{
    using Photon.Deterministic;

    // Walking back for your Accessory patches you up, turning the retrieval trip from pure downside
    // into a small reward.
    //
    // Reacts to OnAccessoryRecovered, which fires only on a real world recovery. That single fact
    // gives the design's three rules for free, with no bookkeeping: one drop passes through recovery
    // exactly once (so it can't be farmed by re-touching the collectible), a Merchant
    // repair/replacement goes through Restore instead (so shopping never heals), and the signal
    // reports the OWNER rather than the collector (so a teammate returning it heals the owner).
    public unsafe class SecondWindMutationData : RiftMutationData
    {
        public FP HealPercentMaxHp = FP._0;

        public override bool IsEligible(Frame f, EntityRef entity) => AccessoryGuardUtility.IsAvailable(f, entity);

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->SecondWindHealPercent = FPMath.Max(stats->SecondWindHealPercent, HealPercentMaxHp);
        }

        protected override object[] DescriptionArgs => new object[] { HealPercentMaxHp.AsFloat * 100f };
    }
}
