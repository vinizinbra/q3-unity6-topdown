namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Second Wind, Flow line B) - recovering after her rhythm is broken.
    //
    //  - Rank 1: breaking Flow grants +20% Move Speed for 1.5s.
    //  - Rank 2: a hit no longer empties the bar - it drops to a third instead of 0.
    //  - Rank 3 "Keep the Beat": a hit taken while Active is 30% weaker, on a 6s cooldown.
    //
    // The whole line is scoped to the moment described by Combat.qtn's OnHostileHitConnected, which
    // means every rank fires for a hit the Accessory Guard or a Free Hit Guard fully negated, exactly
    // as for one that lands. That is deliberate and is the line's entire premise: guarding saves her
    // health, not her groove - and Second Wind is what she buys to soften that.
    //
    // Each rank SETS the totals; they are not additive across ranks.
    public unsafe partial class SecondWindPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Rank 1+ - Move Speed granted the instant a hostile hit breaks Flow (0.20 = +20%). Applied to the shared timed Move Speed slot, so it stacks multiplicatively with the per-Flow-stack bonus rather than fighting it.")]
        public FP[] MoveSpeedBonus = { FP._0_20, FP._0_20, FP._0_20 };

        public FP[] Duration = { FP.FromString("1.5"), FP.FromString("1.5"), FP.FromString("1.5") };

        [Tooltip("Rank 2+ - how much of the Flow bar survives a hostile hit (0.33 = a third), instead of emptying. Flow still switches OFF at every rank - this only decides how far she has to rebuild.")]
        public FP[] ProgressRetainedOnHit = { FP._0, FP.FromString("0.33"), FP.FromString("0.33") };

        [Header("Rank 3 - Keep the Beat")]
        [Tooltip("Damage reduction applied to a hit that connects while Flow is ACTIVE (0.30 = 30% less). Reaches the very hit that triggered it because OnHostileHitConnected is dispatched synchronously above DamageUtility's own resolution steps - and it routes through the shared reactive-DR slot, which is what keeps it from interfering with Accessory durability or Free Hit Guard logic (both sit above DR and have already had their say).")]
        public FP[] DamageReduction = { FP._0, FP._0, FP.FromString("0.30") };

        [Tooltip("Internal cooldown on the reduction above. While it is running, a hit while Active behaves as plain rank 2 - the bar drops to ProgressRetainedOnHit and she takes the hit in full.")]
        public FP[] Cooldown = { FP._0, FP._0, FP._6 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ZaraFlow>(entity, out var flow) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            flow->SecondWindMoveSpeedBonus = MoveSpeedBonus[index];
            flow->SecondWindDuration = Duration[index];
            flow->ProgressRetainedOnHit = ProgressRetainedOnHit[index];
            flow->KeepTheBeatDamageReduction = DamageReduction[index];
            flow->KeepTheBeatCooldown = Cooldown[index];
        }
    }
}
