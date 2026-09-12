namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Resolves shared/coop Talents exactly once, at level start, then resolves every Chunk
    // entity's own ChunkSpawnConfig (e.g. the LobbyStart chunk's own SpawnConfig, spawning
    // whichever starter chests were earned) - see docs/talents.md. Holds until BOTH
    // Global.LevelGenerated (the chunks it positions spawns against have to exist - generation is
    // spread over several ticks, see LevelGenerationSystem.StepGeneration) and every connected
    // player having spawned (PlayerSpawnUtility.HasSpawned). The player wait is the stricter of the
    // two in the normal case and isn't redundant with it - this is the earliest tick every client is
    // guaranteed to have identical, fully-populated RuntimePlayer data for all players, avoiding a
    // determinism hazard from resolving the shared mask before a remote player's join has
    // replicated. Unfiltered SystemMainThread (like LevelGenerationSystem/CombatDirectorSystem)
    // for the resolve step; the spawn sweep right after is a plain filter, not per-tick. Registered
    // in the always-on section of SystemSetup.User.cs, before ChestSystem, so an entity spawned
    // this tick is already visible to that system's own filter this same tick.
    [Preserve]
    public unsafe class TalentGateSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->TalentsResolved == true)
                return;

            // Every spawn resolved below is positioned relative to a Chunk entity, so this can't run
            // before the level actually exists. That used to hold by accident rather than by rule:
            // generation completed entirely inside frame 0 and LevelGenerationSystem is registered
            // ahead of this system, so by the time this ran every chunk was already there. Once
            // generation was spread across ticks (LevelGenerationSystem.StepGeneration) the accident
            // stopped holding - and the player loop below is not a second line of defence, because it
            // `continue`s past a player whose RuntimePlayer hasn't arrived yet instead of holding. On
            // a fresh session's frame 0, with no player data yet, every iteration continued, this fell
            // straight through, latched TalentsResolved for good, and swept a world containing one
            // chunk. LobbyStart - which is where a ChunkSpawnConfig is normally authored - is
            // deliberately placed LAST of all, so its chests never spawned at all.
            if (f.Global->LevelGenerated == false)
                return;

            for (int i = 0; i < f.MaxPlayerCount; i++)
            {
                PlayerRef player = i;

                if (f.GetPlayerData(player) == null)
                    continue;

                if (PlayerSpawnUtility.HasSpawned(f, player) == false)
                    return;
            }

            TalentUtility.ComputeSharedTalents(f);
            f.Global->TalentsResolved = true;

            ResolveSpawners(f);
        }

        private void ResolveSpawners(Frame f)
        {
            var filtered = f.Filter<Chunk, Transform3D>();

            while (filtered.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
            {
                if (chunk.SpawnConfig.Id.IsValid == false)
                    continue;

                ChunkSpawnConfig config = f.FindAsset(chunk.SpawnConfig);

                if (config.Spawns == null)
                    continue;

                for (int i = 0; i < config.Spawns.Length; i++)
                {
                    ResolveSpawn(f, entity, transform, config.Spawns[i], i);
                }
            }
        }

        private void ResolveSpawn(Frame f, EntityRef chunkEntity, Transform3D chunkTransform, SpawnEntityWithRequirement spawner, int index)
        {
            if (TalentUtility.IsSatisfied(f, spawner.Requirement) == false)
                return;

            // Chance <= 0 means "unauthored", not "never" - see SpawnEntityWithRequirement.Chance's
            // own comment.
            if (spawner.Chance > FP._0 && DamageUtility.RollChance(f, spawner.Chance) == false)
                return;

            if (TryPickPrototype(f, spawner.Prototypes, out AssetRef<EntityPrototype> prototype) == false)
            {
                Log.Debug($"[Talents] {chunkEntity}'s ChunkSpawnConfig entry {index} satisfied but no Prototypes assigned - skipping");
                return;
            }

            EntityRef spawned = f.Create(prototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(spawned, out var spawnedTransform))
            {
                // Offset is authored chunk-local (see SpawnEntityWithRequirement's own comment) -
                // has to be rotated by the chunk's own Transform3D.Rotation before adding it to a
                // world position, same as every other chunk-relative offset in this codebase
                // (EnemyPathfindingUtility.WaypointWorldPosition, ChunkCompoundColliderBuilder).
                spawnedTransform->Position = chunkTransform.Position + chunkTransform.Rotation * spawner.Offset;

                // Same "f.Create -> set Position -> GroundOffsetUtility.Apply" pattern every
                // other runtime spawn path in this codebase follows (SpawnedEntitySpawner,
                // CoinUtility, RiftShardUtility, ScrapUtility, ExperienceUtility). Strictly a
                // belt-and-braces re-arm now that a prototype authors GroundOffset.Enabled itself
                // (see GroundOffset.qtn) - it costs nothing and keeps a Chest grounding correctly
                // even if a new prototype ships with that box unticked. No-ops safely if the
                // spawned prototype has no GroundOffset component at all.
                GroundOffsetUtility.Apply(f, spawned);
            }

            Log.Debug($"[Talents] {chunkEntity}'s ChunkSpawnConfig entry {index} spawned {spawned}");
        }

        // Weighted pick among a spawn entry's alternatives - reuses WeightedDrawUtility (the shared
        // implementation new weighted draws in this codebase are meant to use) rather than
        // hand-rolling a second cumulative-weight loop. A single-entry list (the common case)
        // always resolves to that one entry regardless of its Weight.
        private static bool TryPickPrototype(Frame f, WeightedEntityPrototype[] candidates, out AssetRef<EntityPrototype> prototype)
        {
            prototype = default;

            if (candidates == null || candidates.Length == 0)
                return false;

            var weighted = new List<WeightedDrawUtility.Candidate<AssetRef<EntityPrototype>>>(candidates.Length);

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].Prototype.Id.IsValid == false)
                    continue;

                // An unauthored (0) Weight defaults to 1 rather than soft-disabling the candidate -
                // same "0 reads as no-op" convention SpawnEntityWithRequirement.Chance itself follows -
                // so a single-entry list (the common case, e.g. every entry the baker writes) spawns
                // that one entry without anyone having to remember to set a Weight at all.
                int weight = candidates[i].Weight > 0 ? candidates[i].Weight : 1;

                weighted.Add(new WeightedDrawUtility.Candidate<AssetRef<EntityPrototype>> { Value = candidates[i].Prototype, Weight = weight });
            }

            AssetRef<EntityPrototype>[] picked = WeightedDrawUtility.Draw(f, weighted, 1);

            if (picked.Length == 0)
                return false;

            prototype = picked[0];
            return true;
        }
    }
}
