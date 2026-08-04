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

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponRampState>(owner, out var ramp);
            ramp->RampMaxStacks = ramp->RampMaxStacks > MaxStacks ? ramp->RampMaxStacks : MaxStacks;
            ramp->RampDamageBonusPerStack += DamageBonusPerStack;
            ramp->RampFireRateBonusPerStack += FireRateBonusPerStack;
            ramp->RampDecayGrace = FPMath.Max(ramp->RampDecayGrace, DecayGrace);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            DamageBonusPerStack.AsFloat * 100f,
            FireRateBonusPerStack.AsFloat * 100f,
            MaxStacks
        };
    }
}
