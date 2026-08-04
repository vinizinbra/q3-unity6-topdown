namespace Quantum
{
    using Photon.Deterministic;

    // Maintaining this weapon's beam (a fast-repeating Hitscan - this project has no dedicated Beam
    // fire type, see docs/weapon-perks.md) on the same enemy for Threshold seconds applies 1 Rift
    // Mark. Contact progress (Weapon.FocusedBreachTarget/ContactTime) is runtime state tracked in
    // WeaponSystem.FireHitscan - losing contact (a miss, or the hit entity changing) resets it.
    public unsafe class FocusedBreachWeaponPerkData : WeaponPerkData
    {
        public FP Threshold = FP.FromString("1.5");

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponHitTrackingPerks>(owner, out var tracking);
            tracking->HasFocusedBreach = true;
            tracking->FocusedBreachThreshold = tracking->FocusedBreachThreshold <= FP._0
                ? Threshold
                : FPMath.Min(tracking->FocusedBreachThreshold, Threshold);
        }

        protected override object[] DescriptionArgs => new object[] { Threshold.AsFloat };
    }
}
