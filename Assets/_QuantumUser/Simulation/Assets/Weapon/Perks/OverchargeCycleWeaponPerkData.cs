namespace Quantum
{
    using Photon.Deterministic;

    // Feeds Weapon's shared ramp pool (see Weapon.qtn/RelentlessFireWeaponPerkData's own comment) -
    // both damage and fire-rate sides. Stacking this with Relentless Fire/Suppressive Cycle
    // strengthens the one shared ramp rather than running independent ramps, a deliberate design
    // choice (see docs/weapon-perks.md).
    public unsafe class OverchargeCycleWeaponPerkData : WeaponPerkData
    {
        public byte MaxStacks = 8;
        public FP DamageBonusPerStack;
        public FP FireRateBonusPerStack;
        public FP DecayGrace = 1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->RampMaxStacks = weapon->RampMaxStacks > MaxStacks ? weapon->RampMaxStacks : MaxStacks;
            weapon->RampDamageBonusPerStack += DamageBonusPerStack;
            weapon->RampFireRateBonusPerStack += FireRateBonusPerStack;
            weapon->RampDecayGrace = FPMath.Max(weapon->RampDecayGrace, DecayGrace);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            DamageBonusPerStack.AsFloat * 100f,
            FireRateBonusPerStack.AsFloat * 100f,
            MaxStacks
        };
    }
}
