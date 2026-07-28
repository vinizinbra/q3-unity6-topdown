namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DamageMultiplierWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->DamageMultiplier = FPMath.Max(FP._0, weapon->DamageMultiplier * Multiplier);
        }
    }
}
