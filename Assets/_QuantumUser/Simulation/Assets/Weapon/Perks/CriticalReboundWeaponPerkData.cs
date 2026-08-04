namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponPerkReactionSystem.OnCriticalHit - fires a secondary projectile toward the
    // nearest other enemy from the crit's target position. No-op for a Hitscan weapon (nothing to
    // aim a secondary projectile off of), documented simplification.
    public unsafe class CriticalReboundWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 8;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnCritReactions>(owner, out var reactions);
            reactions->HasCriticalRebound = true;
            reactions->CriticalReboundRadius = FPMath.Max(reactions->CriticalReboundRadius, Radius);
            reactions->CriticalReboundDamageMultiplier = FPMath.Max(reactions->CriticalReboundDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
