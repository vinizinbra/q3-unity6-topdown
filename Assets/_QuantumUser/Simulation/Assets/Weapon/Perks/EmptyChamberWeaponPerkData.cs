namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponSystem.ApplyMagazineEmptiedPerks - a knockback-only shockwave (no damage)
    // around the wielder the instant the magazine empties, regardless of how the reload that
    // follows actually plays out.
    public unsafe class EmptyChamberWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 4;
        public FP Knockback = 10;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->HasEmptyChamber = true;
            weapon->EmptyChamberRadius = FPMath.Max(weapon->EmptyChamberRadius, Radius);
            weapon->EmptyChamberKnockback = FPMath.Max(weapon->EmptyChamberKnockback, Knockback);
        }

        protected override object[] DescriptionArgs => new object[] { Radius.AsFloat };
    }
}
