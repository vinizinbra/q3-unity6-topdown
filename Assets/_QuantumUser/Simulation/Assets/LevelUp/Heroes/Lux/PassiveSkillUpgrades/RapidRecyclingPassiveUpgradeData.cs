namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Rapid Recycling, line 2/3) - converts collected Scrap into Sentry
    // AVAILABILITY, which is the other half of Lux's economy alongside Scavenger's supply.
    //
    //  - Ranks 1-2: every Scrap pickup shaves time off the Hero Skill's remaining cooldown.
    //  - Rank 3 "Instant Assembly": earning a Fabrication Charge additionally takes a big chunk off.
    //
    // Deliberately kept separate from the Fabrication Charge itself: a Charge is a FREE DEPLOY
    // regardless of cooldown (SkillSystem.GrantFreeCast), while this reduces the cooldown. Both can be
    // live at once and they don't interfere - which is why rank 3 stacking a cooldown refund on top of
    // earning a Charge is a real payoff rather than a redundant one.
    //
    // Every reduction clamps at 0 (SkillSystem.ReduceCooldown) - nothing ever banks negative cooldown
    // toward a future cast.
    public unsafe partial class RapidRecyclingPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Seconds of remaining Hero Skill cooldown removed per Scrap pickup, per rank.")]
        public FP[] CooldownReductionPerPickup = { FP._0_50, FP._1, FP._1 };

        [Header("Rank 3 - Instant Assembly")]
        [Tooltip("Extra one-off reduction at the moment a Fabrication Charge is actually earned. 0 = not equipped.")]
        public FP[] CooldownReductionOnCharge = { FP._0, FP._0, FP._3 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            collector->CooldownReductionPerPickup = CooldownReductionPerPickup[index];
            collector->CooldownReductionOnCharge = CooldownReductionOnCharge[index];
        }
    }
}
