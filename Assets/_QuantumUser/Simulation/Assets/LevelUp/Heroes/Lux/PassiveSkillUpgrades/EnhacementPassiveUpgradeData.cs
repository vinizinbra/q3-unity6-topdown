namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Scrap pickups also raise the currently-owned Sentry's max health (and
    // heal it by the same amount, so this doesn't read as future-only against a machine already
    // deployed when it's picked) - see ScrapUtility.Grant/ApplyToOwnedSentry.
    public unsafe partial class EnhacementPassiveUpgradeData : PassiveUpgradeData
    {
        public FP MachineHealthBonusPerPickup = 5;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            collector->MachineHealthBonusPerPickup += MachineHealthBonusPerPickup;
        }
    }
}
