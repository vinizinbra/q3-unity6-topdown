namespace Quantum
{
    using Photon.Deterministic;

    // Global tuning for the Coin currency drop - see Coin.qtn, CoinUtility and CoinOrbSystem.
    // Referenced via RuntimeConfig.CoinConfig. Mirrors RiftShardConfig exactly.
    public class CoinConfig : AssetObject
    {
        // Base collection radius for a Coin, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see CoinOrbSystem.
        public FP PickupRadius = 1;

        // How long an uncollected coin lingers before DestroyAfterTime removes it.
        public FP OrbLifetime = 30;

        // Scattered away from the exact death position - same reasoning ScrapConfig/RiftShardConfig
        // already use, so a Coin drop doesn't stack directly on top of an ExpOrb/RiftShard dropped
        // by the same kill. 0 (either field) is a no-op - see CoinUtility.TrySpawnDrop.
        public FP MinSpawnOffset = FP._0;
        public FP MaxSpawnOffset = FP._1_50;
    }
}
