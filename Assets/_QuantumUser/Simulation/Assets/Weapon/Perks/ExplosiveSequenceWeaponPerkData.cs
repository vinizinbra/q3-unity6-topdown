namespace Quantum
{
    using Photon.Deterministic;

    // Every Interval-th shot explodes - the running count (Weapon.ShotsSinceExplosiveProc) is
    // runtime state ticked in WeaponSystem, not authored here.
    public unsafe class ExplosiveSequenceWeaponPerkData : WeaponPerkData
    {
        public int Interval = 5;
        public FP Radius = 3;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->ExplosiveSequenceInterval = weapon->ExplosiveSequenceInterval <= 0
                ? Interval
                : (weapon->ExplosiveSequenceInterval < Interval ? weapon->ExplosiveSequenceInterval : Interval);

            weapon->ExplosiveSequenceRadius = FPMath.Max(weapon->ExplosiveSequenceRadius, Radius);
            weapon->ExplosiveSequenceDamageMultiplier = FPMath.Max(weapon->ExplosiveSequenceDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { Interval, DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
