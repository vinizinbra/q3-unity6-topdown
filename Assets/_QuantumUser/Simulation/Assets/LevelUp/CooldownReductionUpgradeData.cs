namespace Quantum
{
    using Photon.Deterministic;

    // Skill/Dash Cooldown Global Upgrades. Each pick lands the cooldown on an exact authored TOTAL
    // (TotalReduction[pick] - default -15% / -30% / -50%), so the card text is the real effect; the old
    // "multiply the rate by 1.15 per pick" shape advertised -15/-30/-45% but only delivered -13/-24/-34%.
    //
    // The stat itself stays a RATE (StatUtility.GetSkillCooldown divides by it), so a pick multiplies the
    // rate by (1 - previous total) / (1 - next total). That keeps this upgrade's own share exact while
    // still composing multiplicatively with every other cooldown source (Talents, Dead Weight) instead
    // of overwriting them.
    public abstract unsafe class CooldownReductionUpgradeData : GlobalUpgradeData, IRankedUpgrade
    {
        public FP[] TotalReduction = { FP.FromString("0.15"), FP.FromString("0.30"), FP._0_50 };

        public byte MaxRank => (byte)(TotalReduction != null ? TotalReduction.Length : 0);

        protected abstract FP* GetStat(CharacterStats* stats);

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            // GlobalUpgradeUtility.Grant applies BEFORE recording the pick, so this is the number of
            // earlier picks - i.e. the 0-based index of the one being applied now.
            int pick = GlobalUpgradeUtility.GetPickCount(f, entity, new AssetRef<GlobalUpgradeData>(Guid));

            FP* rate = GetStat(stats);
            *rate = FPMath.Max(FP._0, *rate * GetRateStep(pick));
        }

        // Rate factor applied by the 0-based pick-th pick. Public so BalanceSimModel applies the exact
        // same step instead of re-deriving it.
        public FP GetRateStep(int pick)
        {
            if (TotalReduction == null || TotalReduction.Length == 0)
                return FP._1;

            FP previous = pick <= 0 ? FP._0 : GetTotal(pick - 1);
            return (FP._1 - previous) / (FP._1 - GetTotal(pick));
        }

        // Clamped to the last entry (an over-cap pick adds nothing) and below 100%, which would make the
        // rate infinite.
        private FP GetTotal(int index)
        {
            int clamped = index < TotalReduction.Length ? index : TotalReduction.Length - 1;
            return FPMath.Min(TotalReduction[clamped], FP.FromString("0.95"));
        }

        // rank is 1-based (the card shows the rank being picked, the history row the rank owned) and the
        // text is the running total, e.g. "-30% Cooldown" for rank II.
        public string GetDescription(int rank) => DescriptionUtility.Format(Description, new object[] { GetPercent(rank - 1) });

        protected override object[] DescriptionArgs => new object[] { GetPercent(0) };

        private int GetPercent(int index)
        {
            if (TotalReduction == null || TotalReduction.Length == 0)
                return 0;

            return FPMath.RoundToInt(GetTotal(index < 0 ? 0 : index) * 100);
        }
    }
}
