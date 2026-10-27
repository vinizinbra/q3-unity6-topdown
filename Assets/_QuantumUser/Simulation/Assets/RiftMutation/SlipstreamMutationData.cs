namespace Quantum
{
    using Photon.Deterministic;

    // Every Dash grants MoveSpeedBonus Move Speed for Duration seconds (the generic temp-move-speed slot,
    // so chaining Dashes refreshes rather than stacks it).
    public unsafe class SlipstreamMutationData : RiftMutationData
    {
        public FP MoveSpeedBonus = FP.FromString("0.30");
        public FP Duration = 3;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DashMoveSpeedBonus = MoveSpeedBonus;
            stats->DashMoveSpeedDuration = Duration;
        }

        protected override object[] DescriptionArgs => new object[] { MoveSpeedBonus.AsFloat * 100f, Duration.AsFloat };
    }
}
