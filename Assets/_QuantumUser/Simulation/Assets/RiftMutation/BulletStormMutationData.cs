namespace Quantum
{
    using Photon.Deterministic;

    // Sustained bullet output - very long streams of weaker fire, followed by more dangerous reload
    // windows.
    //
    // The magazine half goes through CharacterStats.MagazineSizeBonus rather than writing
    // Weapon.MagazineSize directly, because that field is BAKED at equip and would be wiped by the
    // player's next weapon pickup. WeaponSystem.ApplyOwnerWeaponModifiers re-applies it as a stage
    // of every Equip; calling it here too is what makes the pick take effect on the weapon already
    // in hand instead of only on the next one.
    //
    // ReloadSpeedMultiplier is a RATE (StatUtility.GetReloadDuration divides by it), so a value
    // below 1 correctly makes reloading take LONGER - which is the drawback, not a typo.
    public unsafe class BulletStormMutationData : RiftMutationData
    {
        public FP FireRateMultiplier = FP._1;
        public FP MagazineSizeBonus = FP._0;
        public FP DamageMultiplier = FP._1;
        public FP ReloadSpeedMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AttackSpeedMultiplier = FPMath.Max(FP._0, stats->AttackSpeedMultiplier * FireRateMultiplier);
            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * DamageMultiplier);
            stats->ReloadSpeedMultiplier = FPMath.Max(FP._0, stats->ReloadSpeedMultiplier * ReloadSpeedMultiplier);
            stats->MagazineSizeBonus += MagazineSizeBonus;

            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
            {
                WeaponSystem.ApplyOwnerWeaponModifiers(f, entity, weapon);
            }
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (FireRateMultiplier.AsFloat - 1f) * 100f,
            MagazineSizeBonus.AsFloat * 100f,
            (DamageMultiplier.AsFloat - 1f) * 100f,
            (ReloadSpeedMultiplier.AsFloat - 1f) * 100f
        };
    }
}
