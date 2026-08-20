namespace Quantum
{
    using Photon.Deterministic;

    // Absolute-set tradeoff, not a stacking increment (Rift Mutations are non-stackable pool-wide,
    // see RiftMutationData/RiftMutationPicks) - Shield doubles, Max Health collapses to exactly
    // TargetMaxHealth regardless of any prior Max Health picks. Mirrors MaxHealthUpgradeData's
    // RefreshMax*/multiplier-field shape (ShieldUpgradeData is now flat-additive instead), but
    // assigns MaxHealthMultiplier directly instead of multiplying it further, since "becomes 1" is
    // an absolute target, not a relative increment. See docs/rift-mutations.md.
    public unsafe class GlassCoreMutationData : RiftMutationData
    {
        public FP ShieldMultiplier = FP._1;
        public FP TargetMaxHealth = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MaxShieldMultiplier = FPMath.Max(FP._0, stats->MaxShieldMultiplier * ShieldMultiplier);
            CharacterSystem.RefreshMaxShield(f, entity);

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data != null && data.BaseMaxHealth > FP._0)
            {
                stats->MaxHealthMultiplier = TargetMaxHealth / data.BaseMaxHealth;
                CharacterSystem.RefreshMaxHealth(f, entity);
            }
        }

        protected override object[] DescriptionArgs => new object[] { ShieldMultiplier.AsFloat, TargetMaxHealth.AsFloat };
    }
}
