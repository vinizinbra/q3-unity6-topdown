namespace Quantum
{
    using Photon.Deterministic;

    // Rolled in WeaponSystem.Update right after the primary shot fires - a free extra shot, no
    // extra ammo/cooldown cost. Delay offsets it from the primary shot instead of firing both the
    // same tick - see PendingDoubleTapShot.
    public unsafe class DoubleTapWeaponPerkData : WeaponPerkData
    {
        public FP Chance;
        public FP Delay;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponFireTimeMods>(owner, out var mods);
            mods->DoubleTapChance += Chance;
            mods->DoubleTapDelay = Delay;
        }

        protected override object[] DescriptionArgs => new object[] { Chance.AsFloat * 100f };
    }
}
