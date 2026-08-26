namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Headliner, Flow line C) - the payoff for actually holding Flow.
    //
    //  - Rank 1: +10% outgoing damage while Flow is Active.
    //  - Rank 2: her Totem's Beats are 15% more effective while Flow is Active.
    //  - Rank 3 "Headliner": ACTIVATING Flow fires a short Hype burst - she and nearby allies gain
    //    +10% Move Speed and Fire Rate for 3s, on an 8s cooldown.
    //
    // Ranks 1-2 are conditional-while-Active; rank 3 is a one-shot on the activation edge (see
    // ZaraFlowUtility.SetProgress), never an aura sustained while Active. Its cooldown is what stops a
    // Zara who repeatedly bounces off the top of the bar from keeping the party buff up permanently.
    //
    // Rank 2 is the ONLY intentional Flow/Totem crossover in this design. Flow belongs to Zara, not to
    // her Totem: this makes her rhythm improve a Totem she already planted, and pointedly does NOT
    // make the Totem generate Flow or change its beat frequency.
    //
    // Each rank SETS the totals; they are not additive across ranks.
    public unsafe partial class HeadlinerPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Rank 1+ - outgoing damage bonus while Flow is Active (0.10 = +10%). Applies to every DamageSource, so it lifts her weapon, her Totem's Damage Beats and her Afterbeats alike.")]
        public FP[] ActiveDamageBonus = { FP._0_10, FP._0_10, FP._0_10 };

        [Tooltip("Rank 2+ - Totem Beat effectiveness while Flow is Active (0.15 = +15%): Damage Beats hit harder, Support Beats heal and buff harder. Read LIVE by the beat, so it tracks her current rhythm rather than the moment she planted the Totem. Deliberately does NOT change Beat frequency.")]
        public FP[] ActiveBeatEffectiveness = { FP._0, FP.FromString("0.15"), FP.FromString("0.15") };

        [Header("Rank 3 - Hype")]
        [Tooltip("Radius of the party burst fired when Flow ACTIVATES. 0 at ranks 1-2 disables it entirely.")]
        public FP[] HypeRadius = { FP._0, FP._0, FP._6 };

        public FP[] HypeDuration = { FP._0, FP._0, FP._3 };
        public FP[] HypeMoveSpeedBonus = { FP._0, FP._0, FP._0_10 };
        public FP[] HypeFireRateBonus = { FP._0, FP._0, FP._0_10 };

        [Tooltip("Internal cooldown. Re-activating Flow while this is running must not retrigger Hype - it is a temporary team payoff for a good run of rhythm, not something to be pumped by bouncing off the cap.")]
        public FP[] HypeCooldown = { FP._0, FP._0, FP._8 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ZaraFlow>(entity, out var flow) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            flow->ActiveDamageBonus = ActiveDamageBonus[index];
            flow->ActiveBeatEffectiveness = ActiveBeatEffectiveness[index];
            flow->HypeRadius = HypeRadius[index];
            flow->HypeDuration = HypeDuration[index];
            flow->HypeMoveSpeedBonus = HypeMoveSpeedBonus[index];
            flow->HypeFireRateBonus = HypeFireRateBonus[index];
            flow->HypeCooldown = HypeCooldown[index];
        }
    }
}
