namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases Scrap drop chance. Additive on top of
    // ScrapCollectorPassiveData's own authored DropChance, same "bonus stacks on authored value"
    // shape SpawnRadiusUpgrade/IncreaseDurationUpgrade already use for skill-side ascensions.
    public unsafe partial class EfficientSalvagePassiveUpgradeData : PassiveUpgradeData
    {
        public FP DropChanceBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->DropChance = FPMath.Clamp(collector->DropChance + DropChanceBonus, FP._0, FP._1);
        }
    }
}
