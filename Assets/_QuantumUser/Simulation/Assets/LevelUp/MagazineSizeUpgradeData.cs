namespace Quantum
{
    using Photon.Deterministic;

    // Goes through CharacterStats.MagazineSizeBonus (same pipeline as Bullet Storm) rather than
    // writing Weapon.MagazineSize directly: that field is BAKED at equip, so a direct write was wiped
    // by the next weapon pickup (Choose Weapon, Store). Each pick adds Multiplier - 1 to the bonus
    // (1.2 = +20% per pick, additive across picks). See docs/global-upgrades.md.
    public unsafe class MagazineSizeUpgradeData : GlobalUpgradeData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            WeaponSystem.AddMagazineSizeBonus(f, entity, Multiplier - FP._1);
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FPMath.Abs(Multiplier - FP._1) * 100) };
    }
}
