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

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponRampState>(owner, out var ramp);
            ramp->RampMaxStacks = ramp->RampMaxStacks > MaxStacks ? ramp->RampMaxStacks : MaxStacks;
            ramp->RampFireRateBonusPerStack += FireRateBonusPerStack;
            ramp->RampDecayGrace = FPMath.Max(ramp->RampDecayGrace, DecayGrace);
        }

        protected override object[] DescriptionArgs => new object[] { FireRateBonusPerStack.AsFloat * 100f, MaxStacks };
    }
}
