namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Faster Tempo, Flow line A) - reach Flow faster, and make each stack
    // worth more.
    //
    //  - Rank 1: Flow builds 25% faster.
    //  - Rank 2: 50% faster, and Active Flow is worth +18% instead of +15%.
    //  - Rank 3 "Full Tempo": 75% faster, and Active Flow grants a further +10% Fire Rate on top.
    //
    // Kept its name through the Resonance -> Flow refactor because its ROLE survived intact ("get to
    // the good state sooner"); only the resource it accelerates changed.
    //
    // Each rank SETS the totals; they are not additive across ranks.
    public unsafe partial class FasterTempoPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Multiplies the RATE Flow builds at, per rank (1.25 = 25% faster). ZaraFlowSystem divides the base interval by this rather than overwriting it, so the stated percentage stays true if the baseline interval is ever retuned.")]
        public FP[] BuildRateMultiplier = { FP.FromString("1.25"), FP.FromString("1.50"), FP.FromString("1.75") };

        [Tooltip("Move Speed while Flow is Active, per rank - OVERWRITES the passive's own baseline (0.15). Rank 1 deliberately restates that baseline rather than leaving it alone, so a re-pick at any rank always writes a complete, correct value.")]
        public FP[] MoveSpeedBonus = { FP.FromString("0.15"), FP.FromString("0.18"), FP.FromString("0.18") };

        [Tooltip("Fire Rate while Flow is Active, per rank - same overwrite semantics as MoveSpeedBonus.")]
        public FP[] FireRateBonus = { FP.FromString("0.15"), FP.FromString("0.18"), FP.FromString("0.18") };

        [Tooltip("Rank 3 \"Full Tempo\" - additional Fire Rate on top of FireRateBonus while Active. 0 at ranks 1-2.")]
        public FP[] ActiveFireRateBonus = { FP._0, FP._0, FP._0_10 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ZaraFlow>(entity, out var flow) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            flow->BuildRateMultiplier = BuildRateMultiplier[index];
            flow->MoveSpeedBonus = MoveSpeedBonus[index];
            flow->FireRateBonus = FireRateBonus[index];
            flow->ActiveFireRateBonus = ActiveFireRateBonus[index];

            // The bonus values just changed, so whatever is currently baked into CharacterStats is
            // stale by exactly this pick. Rebaking immediately (rather than waiting for the next
            // toggle) is what stops a Zara who is Active right now from having to break and rebuild her
            // Flow before the upgrade does anything.
            ZaraFlowUtility.ApplyStatBonuses(f, entity, flow);
        }
    }
}
