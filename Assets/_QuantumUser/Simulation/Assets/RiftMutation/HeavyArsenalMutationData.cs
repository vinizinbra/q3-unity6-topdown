namespace Quantum
{
    using Photon.Deterministic;

    // Slow, impactful shooting - every shot hits harder, lands heavier, and comes less often.
    //
    // All four effects are generic character-level stats, so nothing here knows or cares which
    // weapon is equipped: damage and fire rate are live multipliers read per shot, knockback feeds
    // the shared knockback resolution, and the stagger chance is rolled once in
    // DamageUtility.ApplyDamage for every weapon hit alike (see TryApplyWeaponStagger).
    public unsafe class HeavyArsenalMutationData : RiftMutationData
    {
        public FP DamageMultiplier = FP._1;
        public FP FireRateMultiplier = FP._1;
        public FP KnockbackMultiplier = FP._1;
        public FP StaggerChance = FP._0;
        public FP StaggerDuration = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * DamageMultiplier);
            stats->AttackSpeedMultiplier = FPMath.Max(FP._0, stats->AttackSpeedMultiplier * FireRateMultiplier);
            stats->KnockbackMultiplier = FPMath.Max(FP._0, stats->KnockbackMultiplier * KnockbackMultiplier);

            // Take-the-stronger rather than additive, so a future second stagger source can't
            // silently push the chance past 100% - the same composition rule the generic
            // take-the-stronger status buffs already use.
            stats->WeaponStaggerChance = FPMath.Max(stats->WeaponStaggerChance, StaggerChance);
            stats->WeaponStaggerDuration = FPMath.Max(stats->WeaponStaggerDuration, StaggerDuration);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (DamageMultiplier.AsFloat - 1f) * 100f,
            (FireRateMultiplier.AsFloat - 1f) * 100f
        };
    }
}
