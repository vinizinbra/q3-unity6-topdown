namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class ReloadSpeedWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            if (Multiplier <= FP._0)
            {
                Log.Error($"[Weapon] {name} has a non-positive Multiplier ({Multiplier}) - perk ignored");
                return;
            }

            weapon->ReloadDuration = FPMath.Max(FP._0, weapon->ReloadDuration / Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
