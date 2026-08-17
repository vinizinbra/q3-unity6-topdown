namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // One entry in a BreakLootData drop table - "drop this prototype on break, if this shared Talent
    // is met (and its chance rolls)". Same talent-gated shape as ChunkSpawnConfig's
    // SpawnEntityWithRequirement (Prototype/Requirement/Chance), plus the value/count a dropped orb
    // needs. Prototype is any pickup EntityPrototype - a CurrencyOrb (Coin/Exp/RiftShard) or the
    // HealthOrb; Value is stamped onto whichever of those the spawned entity actually carries
    // (CurrencyOrb.Value or HealthOrb.HealAmount), so one drop-table shape covers every pickup type.
    [Serializable]
    public struct BreakDrop
    {
        public AssetRef<EntityPrototype> Prototype;

        // Gate, evaluated by TalentUtility.IsSatisfied - None (the default) always passes. Lets a
        // barrel only drop e.g. a HealthOrb once the run has unlocked that talent.
        public SharedTalentRequirement Requirement;

        // 0-1 probability this still drops once Requirement is satisfied. An unauthored 0 is treated
        // as "no chance gate" (always drops) - same "0 reads as no-op" convention ChunkSpawnConfig's
        // Chance and every other unset multiplier in this codebase follow.
        public FP Chance;

        // Stamped onto CurrencyOrb.Value or HealthOrb.HealAmount of the spawned pickup (whichever it
        // has), so the barrel authors the payout, not the shared orb prototype.
        public FP Value;

        // How many of Prototype to spawn (each scattered independently by SpawnWithPop). <= 0 is
        // treated as 1.
        public Int32 Count;
    }

    // Talent-gated loot table referenced by a SpawnOnBreak component (see SpawnOnBreak.qtn) - resolved
    // once by BreakableUtility.TryBreak when its owner breaks. Same "AssetObject array field, not a
    // component" shape ChunkSpawnConfig/LevelConfig.ChunkPool already use, so one table can be shared
    // across many barrel prototypes. Every satisfied+rolled entry drops; entries are independent (a
    // barrel can drop a coin AND xp AND a talent-gated health orb from one break).
    public class BreakLootData : AssetObject
    {
        public BreakDrop[] Drops;

        // Ring-scatter bounds for every dropped pickup's ballistic pop, forwarded straight to
        // OrbSpawnUtility.SpawnWithPop - same MinSpawnOffset/MaxSpawnOffset shape CoinConfig uses. A
        // MaxSpawnOffset of 0 drops everything on the break point (SpawnWithPop just ground-snaps).
        public FP MinSpawnOffset = FP._0;
        public FP MaxSpawnOffset;

        // Seconds each dropped pickup lingers before DestroyAfterTimeSystem removes it if uncollected.
        // The shared CurrencyOrb prototypes don't author their own DestroyAfterTime (their normal
        // spawn utilities add it per drop - see CoinUtility), so BreakableUtility sets it here from
        // the barrel's own table, mirroring that pattern.
        public FP OrbLifetime = 30;

        // Optional random "burst" velocity added to each dropped pickup's pop on top of the ring-arc
        // scatter, so a pile of drops off one break sprays out organically instead of every one
        // tracing the identical arc - forwarded to OrbSpawnUtility.SpawnWithPop. Both 0 (the default)
        // keeps the plain arc-to-ring behavior. PopHorizontalBurstSpeed is a random-direction ground
        // spread; PopVerticalBurstSpeed is a random upward kick (a taller pop).
        public FP PopHorizontalBurstSpeed;
        public FP PopVerticalBurstSpeed;
    }
}
