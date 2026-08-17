namespace Quantum
{
    using Photon.Deterministic;

    // Global tuning for the HealthOrb pickup - see HealthOrb.qtn and HealthOrbSystem. Referenced via
    // RuntimeConfig.HealthOrbConfig. Mirrors CoinConfig's pickup tunables (no drop-side fields here -
    // a HealthOrb is dropped by a Breakable's loot table, not by an enemy kill, so its spawn
    // scatter/lifetime come from BreakLootData, not this).
    public class HealthOrbConfig : AssetObject
    {
        // Base collection radius, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see HealthOrbSystem.
        public FP PickupRadius = 1;
    }
}
