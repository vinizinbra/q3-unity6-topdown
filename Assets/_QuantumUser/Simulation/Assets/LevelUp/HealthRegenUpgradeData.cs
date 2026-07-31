namespace Quantum
{
    using Photon.Deterministic;

    // Adds a flat FP/sec to Health.RegenRate on grant - stacks with itself and with the hero's own
    // CharacterData.BaseHealthRegenRate (0 for most heroes). See docs/global-upgrades.md.
    public unsafe class HealthRegenUpgradeData : GlobalUpgradeData
    {
        public FP RegenAmount = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            health->RegenRate += RegenAmount;

            Log.Debug($"[LevelUp] {entity} Health Regen +{RegenAmount}/s -> {health->RegenRate}/s");
        }

        protected override object[] DescriptionArgs => new object[] { RegenAmount };
    }
}
