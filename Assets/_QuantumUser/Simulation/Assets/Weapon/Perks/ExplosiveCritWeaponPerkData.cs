namespace Quantum
{
    using Photon.Deterministic;

    // Hellshot's signature - a crit detonates an explosion centered on its own target. Consumed by
    // WeaponPerkReactionSystem.OnCriticalHit, same reaction hook Critical Rebound/Bottomless
    // Momentum share. Authored directly into WeaponDataAsset.BaseTraits today (not offered through
    // the roll pool), but shaped like any other WeaponPerkData so it could be added to
    // WeaponPerkPoolData later with no further work.
    public unsafe class ExplosiveCritWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 3;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnCritReactions>(owner, out var reactions);
            reactions->HasExplosiveCrit = true;
            reactions->CriticalExplosionRadius = FPMath.Max(reactions->CriticalExplosionRadius, Radius);
            reactions->CriticalExplosionDamageMultiplier = FPMath.Max(reactions->CriticalExplosionDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
