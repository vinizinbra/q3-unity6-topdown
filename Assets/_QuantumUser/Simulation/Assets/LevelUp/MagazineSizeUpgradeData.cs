namespace Quantum
{
    using Photon.Deterministic;

    // No CharacterStats equivalent exists for magazine size (Weapon.MagazineSize is a baked
    // absolute, not scaled by any standing multiplier) - targets the exact same field
    // MagazineMultiplierWeaponPerkData does, so a Global Upgrade pick and a Weapon Perk pick stack
    // on the one field rather than needing a parallel do-nothing CharacterStats field. See
    // docs/global-upgrades.md.
    public unsafe class MagazineSizeUpgradeData : GlobalUpgradeData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            int magazineSize = FPMath.RoundToInt(weapon->MagazineSize * Multiplier);
            weapon->MagazineSize = magazineSize < 1 ? 1 : magazineSize;
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FPMath.Abs(Multiplier - FP._1) * 100) };
    }
}
