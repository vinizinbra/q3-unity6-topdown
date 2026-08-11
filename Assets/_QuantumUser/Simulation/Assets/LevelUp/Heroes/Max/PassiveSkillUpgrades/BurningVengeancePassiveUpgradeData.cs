namespace Quantum
{
    using Photon.Deterministic;

    // Passive line 6 - consuming a Vendetta mark (killing the marked target) spreads Burn to nearby
    // enemies, growing radius/targets/strength per rank; rank 3 additionally adds a genuine radial
    // Burn/damage burst at the death position if the kill was already Burning (see
    // MaxVendettaSystem.OnEntityKilled). Composes onto StatusSpreadOnDeath via FPMath.Max/OR, same
    // shared-component idiom RevengeConfig uses - Wildfire (this same folder) composes onto the exact
    // same component for its own any-Burning-death trigger, so picking both stacks the stronger of
    // each field rather than one silently overwriting the other. Also grants CanApplyBurn so
    // Flashpoint becomes eligible.
    public unsafe partial class BurningVengeancePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] Radius = { 4, 5, 5 };
        public FP[] BurnDuration = { 3, 4, 4 };
        public FP[] BurnIntensity = { FP._0_10, FP.FromString("0.15"), FP.FromString("0.15") };
        public int[] MaxTargets = { 2, 4, 4 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
            spread->TriggerOnVendettaKill = true;
            spread->Radius = FPMath.Max(spread->Radius, Radius[index]);
            spread->BurnDuration = FPMath.Max(spread->BurnDuration, BurnDuration[index]);
            spread->BurnIntensity = FPMath.Max(spread->BurnIntensity, BurnIntensity[index]);
            spread->MaxTargets = spread->MaxTargets > MaxTargets[index] ? spread->MaxTargets : MaxTargets[index];
            spread->HasFieryBurst |= rank >= 3;

            f.AddOrGet<CanApplyBurn>(entity, out _);
        }
    }
}
