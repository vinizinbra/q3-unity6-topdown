namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Passive line 6 - Max's SINGLE Burn-spread line, absorbing the old Burning Vengeance (which did
    // the same thing scoped to Vendetta kills, and is deleted). Any kill while the target is Burning
    // spreads Burn to nearby enemies - see MaxFireMasteryReactionSystem.OnEntityKilled, now the one
    // and only trigger path.
    //
    //  - Rank 1: Burning enemies spread Burn when they die.
    //  - Rank 2: wider radius, more targets, and a stronger transferred Burn - all three levers at
    //    once rather than a single scaling number, since the brief asks for expansion of the mechanic
    //    rather than +X%.
    //  - Rank 3: the spread INHERITS the defeated enemy's own remaining Burn (duration and
    //    intensity), scaled by RetainedFractionAtMaxRank, instead of the flat authored values.
    //
    // Recursion safety is structural rather than a runtime guard: TriggerOnAnyBurningDeath lives on
    // the OWNER (not per-enemy) and OnEntityKilled fires once per genuine death event, so a spread
    // can't re-enter within a tick; and RetainedFractionAtMaxRank being < 1 means every jump inherits
    // a strictly weaker fire, so a chain decays instead of sustaining itself.
    public unsafe partial class WildfirePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] Radius = { 4, 6, 6 };
        public FP[] BurnDuration = { 3, 4, 4 };
        public FP[] BurnIntensity = { FP._0_10, FP.FromString("0.18"), FP.FromString("0.18") };
        public int[] MaxTargets = { 2, 5, 5 };

        [Tooltip("Rank 3 - fraction of the dying enemy's own remaining Burn duration/intensity carried to each spread target. Must stay below 1 so a chain decays.")]
        public FP RetainedFractionAtMaxRank = FP.FromString("0.75");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
            spread->TriggerOnAnyBurningDeath = true;
            spread->Radius = Radius[index];
            spread->BurnDuration = BurnDuration[index];
            spread->BurnIntensity = BurnIntensity[index];
            spread->MaxTargets = MaxTargets[index];

            // Set (not FPMath.Max-composed) now that this is the only line writing here - each rank's
            // numbers are cumulative totals, same convention every other ranked Ascension uses.
            spread->WildfireRetainedFraction = rank >= 3 ? RetainedFractionAtMaxRank : FP._0;
        }
    }
}
