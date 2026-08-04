namespace Quantum
{
    using Photon.Deterministic;

    // Feeds Weapon's shared ramp pool (see Weapon.qtn) rather than tracking its own stack -
    // Suppressive Cycle/Overcharge Cycle contribute to the same pool, so stacking more than one
    // ramp perk makes the shared ramp stronger/faster instead of running independent ramps (a
    // deliberate design choice, see docs/weapon-perks.md). RampDecayGrace takes the largest grace
    // window any equipped ramp perk asks for, rather than the sum, so combining perks can't make
    // the ramp decay slower than any single one of them intends.
    public unsafe class RelentlessFireWeaponPerkData : WeaponPerkData
    {
        public byte MaxStacks = 5;
        public FP DamageBonusPerStack;
        public FP DecayGrace = 1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponRampState>(owner, out var ramp);
            ramp->RampMaxStacks = ramp->RampMaxStacks > MaxStacks ? ramp->RampMaxStacks : MaxStacks;
            ramp->RampDamageBonusPerStack += DamageBonusPerStack;
            ramp->RampDecayGrace = FPMath.Max(ramp->RampDecayGrace, DecayGrace);
        }

        protected override object[] DescriptionArgs => new object[] { DamageBonusPerStack.AsFloat * 100f, MaxStacks };
    }
}
