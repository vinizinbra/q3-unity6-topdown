namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by DirectHitData.ApplyTerminalWeaponPerks - spawns Count child projectiles at
    // DamageMultiplier of the original shot's damage once it's actually done flying (pierce/bounces
    // exhausted, or expired).
    public unsafe class SplitShotWeaponPerkData : WeaponPerkData
    {
        public int Count = 2;
        public FP DamageMultiplier = FP._0_50;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->HasSplitShot = true;
            procs->SplitShotCount = procs->SplitShotCount > Count ? procs->SplitShotCount : Count;
            procs->SplitShotDamageMultiplier = FPMath.Max(procs->SplitShotDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { Count, DamageMultiplier.AsFloat * 100f };
    }
}
