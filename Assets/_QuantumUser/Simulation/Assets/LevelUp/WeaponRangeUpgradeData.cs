namespace Quantum
{
    using Photon.Deterministic;

    // Stacks multiplicatively with RangeMultiplierWeaponPerkData on Weapon.RangeMultiplier, but is
    // also kept on CharacterStats.WeaponRangeMultiplier so WeaponSystem.Equip re-applies it after a
    // weapon swap (SeedStats resets Weapon.RangeMultiplier). See docs/global-upgrades.md.
    public unsafe class WeaponRangeUpgradeData : GlobalUpgradeData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            // Recorded on CharacterStats so ApplyOwnerWeaponModifiers re-applies it on every future
            // equip (Weapon.RangeMultiplier is reset by SeedStats), and applied to the weapon in hand
            // now - multiplicative, so the in-hand weapon never gets it twice.
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                FP current = stats->WeaponRangeMultiplier > FP._0 ? stats->WeaponRangeMultiplier : FP._1;
                stats->WeaponRangeMultiplier = FPMath.Max(FP._0, current * Multiplier);
            }

            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            weapon->RangeMultiplier = FPMath.Max(FP._0, weapon->RangeMultiplier * Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FPMath.Abs(Multiplier - FP._1) * 100) };
    }
}
