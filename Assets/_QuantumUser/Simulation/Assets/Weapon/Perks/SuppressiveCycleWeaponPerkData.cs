namespace Quantum
{
    using Photon.Deterministic;

    // Feeds Weapon's shared ramp pool (see Weapon.qtn/RelentlessFireWeaponPerkData's own comment) -
    // fire-rate side. Stacking this with Relentless Fire/Overcharge Cycle strengthens the one
    // shared ramp rather than running independent ramps, a deliberate design choice (see
    // docs/weapon-perks.md).
    public unsafe class SuppressiveCycleWeaponPerkData : WeaponPerkData
    {
        public byte MaxStacks = 5;
        public FP FireRateBonusPerStack;
        public FP DecayGrace = 1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->RampMaxStacks = weapon->RampMaxStacks > MaxStacks ? weapon->RampMaxStacks : MaxStacks;
            weapon->RampFireRateBonusPerStack += FireRateBonusPerStack;
            weapon->RampDecayGrace = FPMath.Max(weapon->RampDecayGrace, DecayGrace);
        }

        protected override object[] DescriptionArgs => new object[] { FireRateBonusPerStack.AsFloat * 100f, MaxStacks };
    }
}
