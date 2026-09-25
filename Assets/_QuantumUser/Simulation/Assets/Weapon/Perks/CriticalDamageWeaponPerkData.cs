namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class CriticalDamageWeaponPerkData : WeaponPerkData
    {
        public FP Bonus;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            // CriticalDamageBonus IS the weapon's crit multiplier and DamageUtility floors it at 1
            // when it's read, so a plain += did nothing on a weapon authored below 1 (SMG/Uzi 0.5,
            // BeamGun/Flamethrower/sentries 0) and under-delivered on one near it. Floor first, so
            // the card's +25% is a real +0.25x on top of whatever the weapon actually crits for.
            weapon->CriticalDamageBonus = FPMath.Max(FP._1, weapon->CriticalDamageBonus) + Bonus;
        }

        protected override object[] DescriptionArgs => new object[] { Bonus.AsFloat * 100f };
    }
}
