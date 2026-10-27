namespace Quantum
{
    using Photon.Deterministic;

    // Landed weapon hits build +DamagePerStack Weapon Damage, at most one stack per StackInterval
    // seconds, up to MaxStacks; DecayTime seconds without a hit drops them all. The interval (not the
    // hit count) sets the pace, so a shotgun's pellets or a piercing shot count once and every weapon
    // reaches the cap in MaxStacks x StackInterval seconds of sustained fire.
    public unsafe class MercilessComboMutationData : RiftMutationData
    {
        public FP DamagePerStack = FP.FromString("0.02");
        public byte MaxStacks = 25;
        public FP DecayTime = 2;
        public FP StackInterval = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->ComboDamagePerStack = DamagePerStack;
            stats->ComboMaxStacks = MaxStacks;
            stats->ComboDecayTime = DecayTime;
            stats->ComboStackInterval = StackInterval;
            stats->ComboStacks = 0;
            stats->ComboGainCooldown = FP._0;
        }

        protected override object[] DescriptionArgs => new object[] { DamagePerStack.AsFloat * 100f, MaxStacks, DecayTime.AsFloat, DamagePerStack.AsFloat * 100f * MaxStacks, StackInterval.AsFloat };
    }
}
