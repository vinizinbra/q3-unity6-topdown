namespace Quantum
{
    using Photon.Deterministic;

    // Passive line 7 - any kill while the target is Burning spreads Burn to nearby enemies, not
    // scoped to a Vendetta-marked kill (see MaxFireMasteryReactionSystem.OnEntityKilled), growing
    // radius/targets per rank. Composes onto the same StatusSpreadOnDeath component Burning Vengeance
    // uses via FPMath.Max - picking both stacks the stronger of each field rather than one
    // overwriting the other. Rank 3 switches the spread's own Burn values from flat-authored to the
    // dying enemy's OWN live Burn (a retained fraction) - "enemies ignited by Wildfire can themselves
    // spread it again" needs zero extra code beyond that, since TriggerOnAnyBurningDeath lives on the
    // owner, not per-enemy, and OnEntityKilled fires once per actual death event so recursion within
    // one call is already structurally impossible.
    public unsafe partial class WildfirePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] Radius = { 4, 5, 5 };
        public FP[] BurnDuration = { 3, 4, 4 };
        public FP[] BurnIntensity = { FP._0_10, FP.FromString("0.15"), FP.FromString("0.15") };
        public int[] MaxTargets = { 2, 4, 4 };
        public FP RetainedFractionAtMaxRank = FP.FromString("0.75");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
            spread->TriggerOnAnyBurningDeath = true;
            spread->Radius = FPMath.Max(spread->Radius, Radius[index]);
            spread->BurnDuration = FPMath.Max(spread->BurnDuration, BurnDuration[index]);
            spread->BurnIntensity = FPMath.Max(spread->BurnIntensity, BurnIntensity[index]);
            spread->MaxTargets = spread->MaxTargets > MaxTargets[index] ? spread->MaxTargets : MaxTargets[index];

            if (rank >= 3)
            {
                spread->WildfireRetainedFraction = RetainedFractionAtMaxRank;
            }
        }
    }
}
