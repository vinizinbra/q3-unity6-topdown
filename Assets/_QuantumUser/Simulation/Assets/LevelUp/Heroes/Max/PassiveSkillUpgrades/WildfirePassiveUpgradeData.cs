namespace Quantum
{
    using Photon.Deterministic;

    // Fire Mastery trait - any kill while the target is Burning spreads Burn to nearby enemies,
    // not scoped to a Vendetta-marked kill (see MaxFireMasteryReactionSystem.OnEntityKilled).
    // Composes onto the same StatusSpreadOnDeath component Burning Vengeance uses via FPMath.Max -
    // picking both stacks the stronger of each field rather than one overwriting the other.
    public unsafe partial class WildfirePassiveUpgradeData : PassiveUpgradeData
    {
        public FP Radius = 4;
        public FP BurnDuration = 3;
        public FP BurnIntensity = FP._0_10;
        public int MaxTargets = 4;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
            spread->TriggerOnAnyBurningDeath = true;
            spread->Radius = FPMath.Max(spread->Radius, Radius);
            spread->BurnDuration = FPMath.Max(spread->BurnDuration, BurnDuration);
            spread->BurnIntensity = FPMath.Max(spread->BurnIntensity, BurnIntensity);
            spread->MaxTargets = spread->MaxTargets > MaxTargets ? spread->MaxTargets : MaxTargets;
        }
    }
}
