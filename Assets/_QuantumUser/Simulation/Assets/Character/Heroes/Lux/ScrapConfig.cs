namespace Quantum
{
    using Photon.Deterministic;

    // Balance tuning for Lux's Scrap pickup - see ScrapUtility/ScrapOrbSystem. Deliberately its own
    // tiny asset rather than folding into ExperienceConfig - Scrap only ever means anything to
    // whoever has the ScrapCollector passive, not the whole co-op run.
    public class ScrapConfig : AssetObject
    {
        public FP PickupRadius = 2;
        public FP OrbLifetime = 30;

        // Scatters a ScrapOrb's spawn point away from the dying enemy's exact position (see
        // ScrapUtility.TrySpawnDrop) - ExpOrb always spawns exactly on that position, so leaving
        // Scrap there too would stack the two pickups directly on top of each other. A ring (not a
        // filled disc), same EnemyMovementUtility.RandomPositionInRing every other scattered spawn in
        // this project already uses (e.g. EnemyDeliveryData.RandomizeAroundAnchor).
        public FP MinSpawnOffset = FP._0_50;
        public FP MaxSpawnOffset = FP._1_50;
    }
}
