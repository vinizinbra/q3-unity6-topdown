namespace Quantum
{
    using Photon.Deterministic;

    // Shots per second scales by Multiplier, so cooldown divides by it - 1.25 is "+25% fire rate".
    // Writes the same FireCooldown as CooldownMultiplierWeaponPerkData; the two differ only in
    // which direction reads naturally when authoring.
    public unsafe class FireRateWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            if (Multiplier <= FP._0)
            {
                Log.Error($"[Weapon] {name} has a non-positive Multiplier ({Multiplier}) - perk ignored");
                return;
            }

            weapon->FireCooldownMultiplier = FPMath.Max(FP._0, weapon->FireCooldownMultiplier / Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
