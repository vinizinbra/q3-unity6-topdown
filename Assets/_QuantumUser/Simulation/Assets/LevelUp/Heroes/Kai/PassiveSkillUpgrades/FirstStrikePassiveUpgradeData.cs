namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (First Strike, line 3/3) - see docs/kai-ascensions.md. Bonus damage on
    // an enemy's FIRST-EVER hit from an owner holding this upgrade, read live in
    // DamageUtility.ResolveOutgoingDamage (see FirstStrikeMark - a RevengeMark-shaped component on the
    // target, tracking which Kai claimed it).
    //
    //  - Ranks 1-2: bigger opening damage.
    //  - Rank 3: killing a First-Strike target banks a one-shot bonus onto the NEXT First Strike (see
    //    KaiFirstStrikeSystem) - the line's payoff is chaining between targets, not re-hitting one.
    //
    // Deliberately no refresh window: an earlier design freed the mark after ~5 untouched seconds so
    // the same enemy could be First-Struck repeatedly, which turned an assassination mechanic into a
    // damage-over-time rotation against a single target. Each enemy triggers this exactly once now.
    public unsafe partial class FirstStrikePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] DamageMultiplierBonus = { FP.FromString("0.40"), FP.FromString("0.70"), FP._1 };

        [Tooltip("Rank 3 only - extra damage on the next First Strike after killing a First-Strike target. Non-stacking: a second kill before it's spent re-arms it rather than doubling it.")]
        public FP[] KillEmpowerBonus = { FP._0, FP._0, FP._0_25 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<FirstStrikeUpgrade>(entity, out var upgrade);
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->KillEmpowerBonus = KillEmpowerBonus[index];
        }
    }
}
