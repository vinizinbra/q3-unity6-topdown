namespace Quantum
{
    // Passive Ascension - Filler-tier enemies can also drop Scrap (the base passive alone starts at
    // Normal+) - see ScrapUtility.TrySpawnDrop's own tier check. Placeholder class name/DisplayName -
    // rename freely, nothing else references "Scavenger" by name.
    public unsafe partial class ScavengerPassiveUpgradeData : PassiveUpgradeData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->IncludeFillerTier = true;
        }
    }
}
