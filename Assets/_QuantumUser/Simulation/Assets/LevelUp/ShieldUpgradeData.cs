namespace Quantum
{
    using Photon.Deterministic;

    // A flat "+N Shield" Global Upgrade (default +10), NOT a percentage - unlike MaxHealthUpgradeData
    // and the rest of CharacterStatMultiplierUpgradeData, this adds to the additive
    // CharacterStats.BonusMaxShield term rather than scaling MaxShieldMultiplier, so the bonus is a
    // fixed capacity regardless of the hero's BaseMaxShield. Shield.Max is baked from
    // BaseMaxShield * MaxShieldMultiplier + BonusMaxShield at seed time, not derived live on read, so
    // RefreshMaxShield has to run right after mutating BonusMaxShield or the change silently does
    // nothing (same reasoning as MaxHealthUpgradeData). See docs/global-upgrades.md.
    public unsafe class ShieldUpgradeData : GlobalUpgradeData
    {
        public FP Amount = FP._10;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->BonusMaxShield = FPMath.Max(FP._0, stats->BonusMaxShield + Amount);
            CharacterSystem.RefreshMaxShield(f, entity);
        }

        // "+{0} Shield" - the flat amount itself, rounded for display (no % now).
        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(Amount) };
    }
}
