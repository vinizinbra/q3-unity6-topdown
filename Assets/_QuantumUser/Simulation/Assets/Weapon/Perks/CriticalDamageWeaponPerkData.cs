namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class CriticalDamageWeaponPerkData : WeaponPerkData
    {
        public FP Bonus;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->CriticalDamageBonus += Bonus;
        }

        protected override object[] DescriptionArgs => new object[] { Bonus.AsFloat * 100f };
    }
}
