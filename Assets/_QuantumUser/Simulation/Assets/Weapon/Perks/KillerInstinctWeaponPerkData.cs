namespace Quantum
{
    using Photon.Deterministic;

    // Restarted to Duration on every kill (WeaponPerkReactionSystem.OnEntityKilled) - the bonus
    // folds into the live fire-cooldown read while the timer is > 0 (WeaponSystem.
    // ResolveLiveFireCooldown), not baked.
    public unsafe class KillerInstinctWeaponPerkData : WeaponPerkData
    {
        public FP FireRateBonus;
        public FP Duration = 2;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnKillReactions>(owner, out var reactions);
            reactions->KillerInstinctFireRateBonus += FireRateBonus;
            reactions->KillerInstinctDuration = FPMath.Max(reactions->KillerInstinctDuration, Duration);
        }

        protected override object[] DescriptionArgs => new object[] { FireRateBonus.AsFloat * 100f, Duration.AsFloat };
    }
}
