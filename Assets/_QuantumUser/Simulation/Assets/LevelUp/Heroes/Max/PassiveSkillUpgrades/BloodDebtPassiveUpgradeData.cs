namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Passive line 5 - Max's Vendetta progression. Composes onto the base Passive's own RevengeConfig
    // (see VendettaPassiveData), same shared-component idiom Brute's Guardian already established
    // for ranked Passives. Each rank SETS the total values (not additive across ranks).
    //
    //  - Rank 1: marked enemies take more damage (RevengeConfig.DamageBonus, base 15%).
    //  - Rank 2: a Vendetta kill passes its mark to the nearest unmarked enemy around the death
    //    (RevengeConfig.MarkSpreadRadius - see MaxVendettaSystem.TrySpreadMark), so one revenge hit
    //    can chain through a pack instead of ending at the first kill.
    //  - Rank 3: every Vendetta kill feeds Rage (RageOnVendettaKill - see
    //    MaxOverdriveReactionSystem.TryRefundRage) and heals a guaranteed slice of Max HP
    //    (RevengeConfig.MinHealFraction, base 1%). The per-kill ceiling (MaxHealFractionPerKill)
    //    stays on from rank 1 so the chained kills can't turn into a healing engine.
    public unsafe partial class BloodDebtPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Bonus damage against Vendetta-marked enemies, per rank (base passive is 0.15).")]
        public FP[] DamageBonus = { FP.FromString("0.30"), FP.FromString("0.30"), FP.FromString("0.30") };

        [Header("Rank 2")]
        [Tooltip("A consumed mark jumps to the nearest enemy within this radius of the kill.")]
        public FP MarkSpreadRadius = 6;

        [Header("Rank 3")]
        [Tooltip("Rage stacks refunded per Vendetta kill.")]
        public byte RageOnVendettaKill = 2;

        [Tooltip("Minimum heal per Vendetta kill, as a fraction of Max's own MaxHealth (base passive is 0.01).")]
        public FP MinHealFractionAtMaxRank = FP.FromString("0.03");

        [Tooltip("Hard ceiling on a single Vendetta kill's heal, as a fraction of Max's own MaxHealth. Applied at every rank, not just rank 3.")]
        public FP MaxHealFractionPerKill = FP.FromString("0.15");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<RevengeConfig>(entity, out var config);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            config->DamageBonus = DamageBonus[index];
            config->MaxHealFractionPerKill = MaxHealFractionPerKill;

            if (rank >= 2)
            {
                config->MarkSpreadRadius = MarkSpreadRadius;
            }

            if (rank >= 3)
            {
                config->RageOnVendettaKill = RageOnVendettaKill;
                config->MinHealFraction = MinHealFractionAtMaxRank;
            }
        }
    }
}
