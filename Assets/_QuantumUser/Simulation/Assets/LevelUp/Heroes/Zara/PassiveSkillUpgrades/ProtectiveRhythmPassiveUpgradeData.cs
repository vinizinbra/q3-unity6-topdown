namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Protective Rhythm, line 2/3) - Zara's defensive support path.
    // Replaces the old "Restorative Beat", which scaled the Pulse's HP healing per rank; this line
    // deliberately never touches healing at all. Zara is support first, healer second - so her
    // defensive investment buys temporary Shield and mitigation, which help a team survive a spike
    // without turning her into a sustain engine that trivialises damage taken.
    //
    //  - Rank 1: the Pulse grants allies a temporary Overshield (fraction of their OWN Max Shield).
    //  - Rank 2: more Overshield, plus a brief damage reduction.
    //  - Rank 3 "Fortissimo": more of both.
    //
    // The damage reduction goes through the shared reactive-DR slot
    // (StatusEffectUtility.ApplyTemporaryDamageReduction) rather than a Zara-owned one, so a co-op
    // stack with Brute's Guardian/Bodyguard resolves through the codebase's generic take-the-stronger
    // policy instead of adding up. Each rank SETS the totals (not additive across ranks).
    public unsafe partial class ProtectiveRhythmPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Temporary Overshield granted per Pulse, as a fraction of the ALLY's own Max Shield.")]
        public FP[] OvershieldPercentOfMaxShield = { FP._0_10, FP.FromString("0.15"), FP._0_20 };

        [Tooltip("How far above an ally's own Max Shield this Overshield may stack Current - the ceiling that stops repeated Pulses banking a second health bar.")]
        public FP OvershieldCapMultiplier = FP._1_50;

        [Tooltip("Rank 2+ - incoming damage reduction granted per Pulse. Refreshes rather than stacks (take-the-stronger).")]
        public FP[] DamageReductionAmount = { FP._0, FP._0_10, FP._0_20 };
        public FP DamageReductionDuration = FP._2;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            resonance->OvershieldPercentOfMaxShield = OvershieldPercentOfMaxShield[index];
            resonance->OvershieldCapMultiplier = OvershieldCapMultiplier;
            resonance->DamageReductionAmount = DamageReductionAmount[index];
            resonance->DamageReductionDuration = DamageReductionDuration;
        }
    }
}
