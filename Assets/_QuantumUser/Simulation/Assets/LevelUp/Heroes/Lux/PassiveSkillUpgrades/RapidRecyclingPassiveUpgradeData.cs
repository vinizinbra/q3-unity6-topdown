namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - each Scrap pickup also reduces the Hero Skill's cooldown by a flat
    // amount, on top of building toward the base passive's own free-charge stack (see
    // ScrapUtility.Grant). This was originally the base passive's own behavior - moved here once the
    // base passive's real payoff became the 10-stack free charge instead.
    public unsafe partial class RapidRecyclingPassiveUpgradeData : PassiveUpgradeData
    {
        public FP CooldownReductionPerPickup = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->CooldownReductionPerPickup += CooldownReductionPerPickup;
        }
    }
}
