namespace Quantum
{
    using Photon.Deterministic;

    // Vendetta Upgrade - consuming a Vendetta mark (killing the marked target) also spreads Burn
    // to nearby enemies. Composes onto StatusSpreadOnDeath via FPMath.Max, same shared-component
    // idiom RevengeConfig uses - Wildfire (a Fire Mastery trait, see WildfirePassiveUpgradeData in
    // this same folder) composes onto the exact same component for its own any-Burning-death
    // trigger, so picking both stacks the stronger of each field rather than one silently
    // overwriting the other.
    public unsafe partial class BurningVengeancePassiveUpgradeData : PassiveUpgradeData
    {
        public FP Radius = 4;
        public FP BurnDuration = 3;
        public FP BurnIntensity = FP._0_10;
        public int MaxTargets = 4;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
            spread->TriggerOnVendettaKill = true;
            spread->Radius = FPMath.Max(spread->Radius, Radius);
            spread->BurnDuration = FPMath.Max(spread->BurnDuration, BurnDuration);
            spread->BurnIntensity = FPMath.Max(spread->BurnIntensity, BurnIntensity);
            spread->MaxTargets = spread->MaxTargets > MaxTargets ? spread->MaxTargets : MaxTargets;
        }
    }
}
