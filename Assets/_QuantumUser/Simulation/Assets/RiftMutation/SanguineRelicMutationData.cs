namespace Quantum
{
    using Photon.Deterministic;

    // +Max Health and lifesteal (capped per second so a high fire-rate weapon can't out-heal anything),
    // but every hit taken lands harder. Lifesteal itself is MutationCombatUtility.TryLifeSteal - the
    // first real reader of CharacterStats.LifeSteal.
    public unsafe class SanguineRelicMutationData : RiftMutationData
    {
        public FP HealthMultiplier = FP.FromString("1.40");
        public FP LifeSteal = FP.FromString("0.05");
        public FP LifeStealCapPerSecond = FP.FromString("0.02");
        public FP DamageTakenFactor = FP.FromString("1.30");

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MaxHealthMultiplier = FPMath.Max(FP._0, stats->MaxHealthMultiplier * HealthMultiplier);
            CharacterSystem.RefreshMaxHealth(f, entity);

            stats->LifeSteal += LifeSteal;
            stats->LifeStealCapPerSecond = FPMath.Max(stats->LifeStealCapPerSecond, LifeStealCapPerSecond);
            stats->DamageTakenMultiplier = FPMath.Max(FP._0, stats->DamageTakenMultiplier * DamageTakenFactor);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (HealthMultiplier.AsFloat - 1f) * 100f, LifeSteal.AsFloat * 100f, LifeStealCapPerSecond.AsFloat * 100f, (DamageTakenFactor.AsFloat - 1f) * 100f
        };
    }
}
