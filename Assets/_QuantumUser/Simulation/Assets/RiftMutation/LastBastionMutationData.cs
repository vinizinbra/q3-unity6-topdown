namespace Quantum
{
    using Photon.Deterministic;

    // Pure HP survival, the exact opposite trade to Glass Core: a much larger health pool, and no
    // Accessory at all.
    //
    // The Accessory is removed via an explicit availability flag (AccessoryGuardUtility.Disable)
    // rather than by pinning durability at 0 - that flag is what makes the Store correctly stop
    // offering a repair/replacement this player could never benefit from, instead of endlessly
    // selling them a defence that would be disabled again a moment later.
    public unsafe class LastBastionMutationData : RiftMutationData
    {
        public FP HealthMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MaxHealthMultiplier = FPMath.Max(FP._0, stats->MaxHealthMultiplier * HealthMultiplier);
            CharacterSystem.RefreshMaxHealth(f, entity);

            AccessoryGuardUtility.Disable(f, entity);
        }

        protected override object[] DescriptionArgs => new object[] { (HealthMultiplier.AsFloat - 1f) * 100f };
    }
}
