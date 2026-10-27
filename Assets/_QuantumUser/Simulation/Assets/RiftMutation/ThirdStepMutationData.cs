namespace Quantum
{
    // Every Interval-th Dash hands its charge straight back. Deliberately a count, not a chance - a
    // mobility tool you can plan around instead of a coin flip on your escape button.
    public unsafe class ThirdStepMutationData : RiftMutationData
    {
        public byte Interval = 3;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DashRefundInterval = Interval;
            stats->DashRefundProgress = 0;
        }

        protected override object[] DescriptionArgs => new object[] { Interval };
    }
}
