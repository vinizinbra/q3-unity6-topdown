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

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->ExplosiveSequenceInterval = procs->ExplosiveSequenceInterval <= 0
                ? Interval
                : (procs->ExplosiveSequenceInterval < Interval ? procs->ExplosiveSequenceInterval : Interval);

            procs->ExplosiveSequenceRadius = FPMath.Max(procs->ExplosiveSequenceRadius, Radius);
            procs->ExplosiveSequenceDamageMultiplier = FPMath.Max(procs->ExplosiveSequenceDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { Interval, DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
