namespace Quantum
{
    using Photon.Deterministic;

    // Character-level mirror of HeavyCaliberWeaponPerkData's own tradeoff shape, targeting
    // CharacterStats.WeaponDamageMultiplier/AttackSpeedMultiplier instead of Weapon's own fields -
    // stacks with that perk rather than replacing it (same independent-sources convention every
    // other Weapon-category Rift Mutation already uses). See docs/rift-mutations.md.
    public unsafe class HeavyArsenalMutationData : RiftMutationData
    {
        public FP DamageMultiplier = FP._1;
        public FP FireRateMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * DamageMultiplier);
            stats->AttackSpeedMultiplier = FPMath.Max(FP._0, stats->AttackSpeedMultiplier * FireRateMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (DamageMultiplier.AsFloat - 1f) * 100f,
            (FireRateMultiplier.AsFloat - 1f) * 100f
        };
    }
}
