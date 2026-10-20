namespace Quantum
{
    using Photon.Deterministic;

    // Global tuning for the Coin currency drop - see Coin.qtn, CoinUtility and CurrencyOrbSystem.
    // Referenced via OverallGameConfig.CoinConfig. Mirrors RiftShardConfig exactly.
    public class CoinConfig : AssetObject
    {
        // Base collection radius for a Coin, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see CurrencyOrbSystem.
        public FP PickupRadius = 1;

        // How long an uncollected coin lingers before DestroyAfterTime removes it.
        // Deliberately longer than the ~3 min between Breathing Breaks: every orb still on the map when a
        // Break is secured gets swept in (CurrencyOrbVacuumUtility), so a missed orb is only lost by dying
        // before the next Break - not by walking past it.
        public FP OrbLifetime = 240;

        // Scattered away from the exact death position - same reasoning ScrapConfig/RiftShardConfig
        // already use, so a Coin drop doesn't stack directly on top of an ExpOrb/RiftShard dropped
        // by the same kill. 0 (either field) is a no-op - see CoinUtility.TrySpawnDrop.
        public FP MinSpawnOffset = FP._0;
        public FP MaxSpawnOffset = FP._1_50;
    }
}
