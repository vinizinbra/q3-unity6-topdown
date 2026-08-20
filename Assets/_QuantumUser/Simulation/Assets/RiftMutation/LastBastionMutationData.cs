namespace Quantum
{
    using Photon.Deterministic;

    // Mirror of GlassCoreMutationData in the opposite direction - Health doubles, Shield is removed
    // outright. Directly zeroes Shield.Max/Current rather than going through
    // CharacterSystem.RefreshMaxShield - that method's newMax <= 0 guard exists to protect against
    // an *unintentional* zero (see its own comment), this one is deliberate. See
    // docs/rift-mutations.md.
    public unsafe class LastBastionMutationData : RiftMutationData
    {
        public FP HealthMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MaxHealthMultiplier = FPMath.Max(FP._0, stats->MaxHealthMultiplier * HealthMultiplier);
            CharacterSystem.RefreshMaxHealth(f, entity);

            stats->MaxShieldMultiplier = FP._0;
            stats->BonusMaxShield = FP._0; // else a flat ShieldUpgradeData pick would revive shield on a later RefreshMaxShield

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == true)
            {
                shield->Max = FP._0;
                shield->Current = FP._0;
            }
        }

        protected override object[] DescriptionArgs => new object[] { (HealthMultiplier.AsFloat - 1f) * 100f };
    }
}
