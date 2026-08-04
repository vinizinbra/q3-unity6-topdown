namespace Quantum
{
    using Photon.Deterministic;

    // Demolition Mastery Hero Trait - a critical hit that is ALSO an explosion applies Burn to
    // whatever it crit (see PixieDemolitionMasterySystem.OnExplosionCriticalHit, fired from
    // DamageUtility.ApplyDamage only when isCritical and isExplosion are both true). BurnIntensity is
    // a flat damage-per-tick value, same convention StatusSpreadOnDeath.BurnIntensity already uses -
    // see Heroes/Pixie/DemolitionMastery.qtn.
    public unsafe partial class VolatilePayloadPassiveUpgradeData : PassiveUpgradeData
    {
        public FP BurnDuration = 3;
        public FP BurnIntensity = 5;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<VolatilePayloadUpgrade>(entity, out var upgrade);
            upgrade->BurnDuration = FPMath.Max(upgrade->BurnDuration, BurnDuration);
            upgrade->BurnIntensity += BurnIntensity;
        }
    }
}
