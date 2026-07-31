namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponPerkReactionSystem.OnCriticalHit - a chance to restore a flat amount of
    // ammo on every crit.
    public unsafe class BottomlessMomentumWeaponPerkData : WeaponPerkData
    {
        public FP Chance = FP._0_50;
        public int Amount = 1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->CritAmmoRestoreChance += Chance;
            weapon->CritAmmoRestoreAmount += Amount;
        }

        protected override object[] DescriptionArgs => new object[] { Chance.AsFloat * 100f, Amount };
    }
}
