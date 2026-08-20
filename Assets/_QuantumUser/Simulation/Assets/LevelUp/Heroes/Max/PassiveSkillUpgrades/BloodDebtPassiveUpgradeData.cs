namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Passive line 5 - Max's Vendetta progression, merging the old standalone Blood Debt + Unbroken
    // Spirit + Settled Score picks into one 3-rank Ascension. All three compose onto the base
    // Passive's own RevengeConfig (see VendettaPassiveData), same shared-component idiom Brute's
    // Guardian already established for ranked Passives. Each rank SETS the total values (not additive
    // across ranks).
    //
    //  - Rank 1: a longer mark window (12s), so a Vendetta actually survives long enough to collect.
    //  - Rank 2: a Vendetta kill refunds Rage (RageOnVendettaKill - see
    //    MaxOverdriveReactionSystem.TryRefundRage), tying the revenge loop into Overdrive uptime, and
    //    Shield damage starts qualifying for marking/accumulation (ShieldDamageCountsForRevenge) so a
    //    shielded Max isn't locked out of his own passive.
    //  - Rank 3: better on-kill sustain (HealMultiplier), deliberately paired with a hard per-kill
    //    ceiling (MaxHealFractionPerKill) rather than left open - the brief is explicit that this
    //    must not become a major healing engine, so the multiplier goes up AND the cap exists, both
    //    editable.
    public unsafe partial class BloodDebtPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("How long a Vendetta mark lasts, per rank.")]
        public FP[] MarkDuration = { 12, 12, 12 };

        [Header("Rank 2")]
        [Tooltip("Rage stacks refunded per Vendetta kill.")]
        public byte RageOnVendettaKill = 2;

        [Header("Rank 3")]
        public FP HealMultiplierAtMaxRank = FP.FromString("0.60");

        [Tooltip("Hard ceiling on a single Vendetta kill's heal, as a fraction of Max's own MaxHealth. Applied at every rank, not just rank 3.")]
        public FP MaxHealFractionPerKill = FP.FromString("0.15");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<RevengeConfig>(entity, out var config);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            config->MarkDuration = MarkDuration[index];
            config->MaxHealFractionPerKill = MaxHealFractionPerKill;

            if (rank >= 2)
            {
                config->RageOnVendettaKill = RageOnVendettaKill;
                f.AddOrGet<ShieldDamageCountsForRevenge>(entity, out _);
            }

            if (rank >= 3)
            {
                config->HealMultiplier = HealMultiplierAtMaxRank;
            }
        }
    }
}
