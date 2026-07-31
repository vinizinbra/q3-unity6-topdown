namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponPerkReactionSystem.OnEntityKilled - restores a fraction of the magazine on
    // any kill, regardless of current ammo (doesn't need the magazine to actually be empty).
    public unsafe class PredatorMagazineWeaponPerkData : WeaponPerkData
    {
        public FP RestoreFraction = FP._0_10;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->HasPredatorMagazine = true;
            weapon->PredatorMagazineRestoreFraction += RestoreFraction;
        }

        protected override object[] DescriptionArgs => new object[] { RestoreFraction.AsFloat * 100f };
    }
}
