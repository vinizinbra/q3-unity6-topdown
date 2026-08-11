namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Passive Ascension (First Strike, line 3/3) - see docs/kai-ascensions.md. Bonus damage on
    // an enemy's first-ever hit from an owner holding this upgrade, read live in
    // DamageUtility.ResolveOutgoingDamage (see FirstStrikeMark - a RevengeMark-shaped component,
    // replacing the old bare KaiFirstStruck tag). Rank 3 "Perfect Opening" additionally lets the mark
    // refresh after RefreshWindow seconds pass without Kai damaging that specific enemy (see
    // FirstStrikeMarkTimeoutSystem).
    public unsafe partial class FirstStrikePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] DamageMultiplierBonus = { FP.FromString("0.40"), FP.FromString("0.70"), FP._1 };

        // Rank 3 only (0 at ranks 1-2, which leaves a mark permanent - "never removed" - exactly like
        // the pre-refactor behavior).
        public FP[] RefreshWindow = { FP._0, FP._0, FP._5 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<FirstStrikeUpgrade>(entity, out var upgrade);
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->RefreshWindow = RefreshWindow[index];
        }
    }
}
