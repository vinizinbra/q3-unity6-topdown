namespace Quantum
{
    using Photon.Deterministic;

    // Dashing heals back RewindFraction of the Health damage you took in the last Window seconds - a
    // Dash that "undoes" the hit you just ate. See MutationCombatUtility.OnDashActivated.
    public unsafe class TimeSlipMutationData : RiftMutationData
    {
        public FP RewindFraction = FP._0_50;
        public FP Window = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DashRewindFraction = RewindFraction;
            stats->DashRewindWindow = Window;
        }

        protected override object[] DescriptionArgs => new object[] { RewindFraction.AsFloat * 100f, Window.AsFloat };
    }
}
