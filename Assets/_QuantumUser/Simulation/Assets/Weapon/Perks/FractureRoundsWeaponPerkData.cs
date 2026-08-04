namespace Quantum
{
    // Every Interval-th confirmed hit from this weapon applies 1 Rift Mark - the running count
    // (Weapon.FractureHitCounter) is runtime state, incremented in
    // WeaponPerkReactionSystem.OnWeaponHitLanded, not authored here. See docs/weapon-perks.md.
    public unsafe class FractureRoundsWeaponPerkData : WeaponPerkData
    {
        public byte Interval = 6;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponHitTrackingPerks>(owner, out var tracking);
            tracking->HasFractureRounds = true;
            tracking->FractureRoundsInterval = tracking->FractureRoundsInterval == 0
                ? Interval
                : (tracking->FractureRoundsInterval < Interval ? tracking->FractureRoundsInterval : Interval);
        }

        protected override object[] DescriptionArgs => new object[] { Interval };
    }
}
