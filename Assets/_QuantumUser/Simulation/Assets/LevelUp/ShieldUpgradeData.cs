namespace Quantum
{
    using Photon.Deterministic;

    // Shield.Max is baked from CharacterStats.MaxShieldMultiplier at seed time, not derived live on
    // read - see MaxHealthUpgradeData for why RefreshMaxShield has to run right after mutating the
    // multiplier. See docs/global-upgrades.md.
    public unsafe class ShieldUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->MaxShieldMultiplier;

        public override void Apply(Frame f, EntityRef entity)
        {
            base.Apply(f, entity);
            CharacterSystem.RefreshMaxShield(f, entity);
        }
    }
}
