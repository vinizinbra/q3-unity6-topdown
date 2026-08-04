namespace Quantum
{
    using Photon.Deterministic;

    // Rolled in WeaponSystem.Update right after the primary shot fires - a free extra shot, no
    // extra ammo/cooldown cost.
    public unsafe class DoubleTapWeaponPerkData : WeaponPerkData
    {
        public FP Chance;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponFireTimeMods>(owner, out var mods);
            mods->DoubleTapChance += Chance;
        }

        protected override object[] DescriptionArgs => new object[] { Chance.AsFloat * 100f };
    }
}
