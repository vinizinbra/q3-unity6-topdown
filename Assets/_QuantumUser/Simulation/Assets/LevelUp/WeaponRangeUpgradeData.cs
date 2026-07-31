namespace Quantum
{
    using Photon.Deterministic;

    // No CharacterStats equivalent exists for weapon range - targets the exact same
    // Weapon.RangeMultiplier field RangeMultiplierWeaponPerkData does, same reasoning as
    // MagazineSizeUpgradeData. See docs/global-upgrades.md.
    public unsafe class WeaponRangeUpgradeData : GlobalUpgradeData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            weapon->RangeMultiplier = FPMath.Max(FP._0, weapon->RangeMultiplier * Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FPMath.Abs(Multiplier - FP._1) * 100) };
    }
}
