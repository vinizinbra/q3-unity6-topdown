namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class CriticalChanceWeaponPerkData : WeaponPerkData
    {
        public FP Chance;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->CriticalChance += Chance;
        }

        protected override object[] DescriptionArgs => new object[] { Chance.AsFloat * 100f };
    }
}
