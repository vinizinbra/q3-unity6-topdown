namespace Quantum
{
    using Photon.Deterministic;

    // Health.MaxHealth is baked from CharacterStats.MaxHealthMultiplier at seed time, not derived
    // live on read - CharacterSystem.RefreshMaxHealth has to run after mutating the multiplier or
    // the change silently does nothing (same reasoning as any other mid-run write to it, see that
    // method's own comment). See docs/global-upgrades.md.
    public unsafe class MaxHealthUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->MaxHealthMultiplier;

        public override void Apply(Frame f, EntityRef entity)
        {
            base.Apply(f, entity);
            CharacterSystem.RefreshMaxHealth(f, entity);
        }
    }
}
