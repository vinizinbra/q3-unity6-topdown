namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // One entry in ChunkSpawnConfig.Spawns - "spawn this entity here if this shared Talent is
    // met". Was a qtn `component` (SpawnEntityWithRequirement, one instance per entity - a real
    // ECS limit once a single LobbyStart chunk needed more than one independent conditional
    // spawn, e.g. WeaponChest + HeroChest + GlobalUpgradeChest all at once). Plain C# struct
    // instead, same "AssetObject array field, not a component" shape LevelConfig.ChunkPool
    // (ChunkPoolEntry[]) already uses - lets one Chunk reference as many of these as it needs via
    // a single AssetRef<ChunkSpawnConfig>.
    [Serializable]
    public struct SpawnEntityWithRequirement
    {
        public AssetRef<EntityPrototype> Prototype;
        public FPVector3 Offset;
        public SharedTalentRequirement Requirement;

        // 0-1 probability this still spawns once Requirement is satisfied. An unauthored 0 is
        // deliberately treated as "no chance gate" (always spawns) by TalentGateSystem rather
        // than "never spawns" - same "0 reads as no-op" convention every other unset multiplier
        // in this codebase follows. Author an actual value in (0, 1) only when a spawn should
        // also be rare.
        public FP Chance;
    }

    // Referenced from Chunk.SpawnConfig (Chunk.qtn) - TalentGateSystem resolves every entry in
    // Spawns exactly once, at level start, for whichever Chunk entity it's assigned to (typically
    // the LobbyStart chunk), and f.Create's Prototype at (that chunk entity's own
    // Transform3D.Position + Offset) if satisfied. See docs/talents.md.
    public class ChunkSpawnConfig : AssetObject
    {
        public SpawnEntityWithRequirement[] Spawns;
    }
}
