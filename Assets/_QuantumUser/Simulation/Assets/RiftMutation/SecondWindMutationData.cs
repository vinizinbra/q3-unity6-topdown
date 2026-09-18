namespace Quantum
{
    using Photon.Deterministic;

    // Walking back for your Accessory patches you up AND gets you moving again, turning the retrieval
    // trip from pure downside into a small reward.
    //
    // Reacts to OnAccessoryRecovered, which fires only on a real world recovery. That single fact
    // gives the design's rules for free, with no bookkeeping: one drop passes through recovery
    // exactly once (so it can't be farmed by re-touching the collectible), a Merchant
    // repair/replacement goes through Restore instead (so shopping never heals or buffs), and the
    // signal reports the OWNER rather than the collector (so a teammate returning it heals/buffs the
    // owner). The Move Speed half rides the generic timed-buff slot, so recovering again while it's
    // still up REFRESHES the duration rather than stacking it.
    public unsafe class SecondWindMutationData : RiftMutationData
    {
        public FP HealPercentMaxHp = FP._0;
        public FP MoveSpeedBonus = FP._0;
        public FP MoveSpeedDuration = FP._0;

        public override bool IsEligible(Frame f, EntityRef entity) => AccessoryGuardUtility.IsAvailable(f, entity);

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->SecondWindHealPercent = FPMath.Max(stats->SecondWindHealPercent, HealPercentMaxHp);
            stats->SecondWindMoveSpeedBonus = FPMath.Max(stats->SecondWindMoveSpeedBonus, MoveSpeedBonus);
            stats->SecondWindMoveSpeedDuration = FPMath.Max(stats->SecondWindMoveSpeedDuration, MoveSpeedDuration);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            HealPercentMaxHp.AsFloat * 100f,
            MoveSpeedBonus.AsFloat * 100f,
            MoveSpeedDuration.AsFloat
        };
    }
}
