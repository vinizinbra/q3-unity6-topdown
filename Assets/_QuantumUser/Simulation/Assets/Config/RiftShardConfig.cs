namespace Quantum
{
    using Photon.Deterministic;

    // Global tuning for the Rift Shard currency drop - see RiftShard.qtn, RiftShardUtility and
    // CurrencyOrbSystem. Referenced via OverallGameConfig.RiftShardConfig. Mirrors ExperienceConfig
    // minus the leveling curve - no leveling is attached to this currency.
    public class RiftShardConfig : AssetObject
    {
        // Base collection radius for a RiftShard, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see CurrencyOrbSystem.
        public FP PickupRadius = 1;

        // How long an uncollected shard lingers before DestroyAfterTime removes it. Deliberately longer
        // than the ~3 min between Breathing Breaks: every shard still on the map when a Break is
        // secured gets swept in (CurrencyOrbVacuumUtility), so a missed shard is only lost by dying
        // before the next Break - not by walking past it.
        public FP OrbLifetime = 240;

        // Scattered away from the exact death position - same reasoning ScrapConfig's own
        // Min/MaxSpawnOffset already uses (so a Rift Shard drop doesn't stack directly on top of an
        // ExpOrb dropped by the same kill). 0 (either field) is a no-op - see
        // RiftShardUtility.TrySpawnDrop.
        public FP MinSpawnOffset = FP._0;
        public FP MaxSpawnOffset = FP._1_50;
    }
}
