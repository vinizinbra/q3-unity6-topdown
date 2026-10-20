namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Boss death cinematic, sim side. The killing blow on the LAST living boss (BossDefeatUtility.
    // TryBegin, from DamageUtility.ResolveDeath) hard-pauses GameplaySystemGroup - the same
    // mechanism the boss intro uses (RunPhaseUtility.BeginBossEncounter) - so nobody can act, take
    // damage or go down while it plays. This always-on system then runs the timeline:
    //   first tick  - clears the arena (every other enemy pops, enemy projectiles/hazards vanish)
    //   BurstTime   - grants the Rift Shard reward + every loose shard pickup to every player + BossDefeatBurst
    //   Duration    - destroys the boss and re-enables the group, so VictorySystem (registered
    //                 after this) sees the boss gone on this same tick.
    // Every visual beat is view-side, driven by the BossDefeated/BossDefeatBurst events.
    //
    // Lives outside GameplaySystemGroup for the same reason BossPauseSystem does: it's the one
    // re-enabling it.
    [Preserve]
    public unsafe class BossDefeatSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var filter = f.Filter<BossDefeatSequence>();

            while (filter.NextUnsafe(out EntityRef boss, out BossDefeatSequence* sequence))
            {
                if (sequence->ArenaCleared == false)
                {
                    BossDefeatUtility.ClearArena(f, boss);
                    sequence->ArenaCleared = true;
                }

                sequence->Elapsed += f.DeltaTime;

                if (sequence->BurstFired == false && sequence->Elapsed >= sequence->BurstTime)
                {
                    sequence->BurstFired = true;

                    // The boss reward plus every Rift Shard pickup still lying around - the run is about to
                    // end, so anything uncollected would otherwise just be lost. One grant, one shower.
                    FP total = sequence->RiftShardReward + BossDefeatUtility.CollectLooseRiftShards(f);

                    if (total > FP._0)
                    {
                        RiftShardUtility.GrantAll(f, total);
                    }

                    FPVector3 position = f.Unsafe.TryGetPointer<Transform3D>(boss, out var transform) ? transform->Position : FPVector3.Zero;
                    f.Events.BossDefeatBurst(boss, position, total);
                    Log.Debug($"[Boss] {boss} defeat burst - granted {total} Rift Shards to every player ({sequence->RiftShardReward} reward + loose pickups)");
                }

                if (sequence->Elapsed >= sequence->Duration)
                {
                    f.Destroy(boss);
                    f.SystemEnable<GameplaySystemGroup>();
                    Log.Debug($"[Boss] {boss} defeat sequence finished - GameplaySystemGroup re-enabled");
                }
            }
        }
    }

    public static unsafe class BossDefeatUtility
    {
        // Called from DamageUtility.ResolveDeath right after a lingering-tier enemy flips to Dead.
        // Only starts the cinematic for the last living boss - a twin boss dying first just lingers
        // and disappears as before.
        public static void TryBegin(Frame f, EntityRef boss, EntityRef killer)
        {
            if (f.Has<BossRuntimeState>(boss) == false || f.Has<BossDefeatSequence>(boss) == true)
                return;

            if (IsAnotherBossAlive(f, boss) == true)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(boss, out var enemy) == false || f.FindAsset(enemy->EnemyData) is not BossDataAsset data)
                return;

            FP duration = FPMath.Max(FP._0, data.DefeatSequenceDuration);

            f.Add(boss, new BossDefeatSequence
            {
                Duration = duration,
                BurstTime = FPMath.Clamp(data.DefeatBurstTime, FP._0, duration),
                RiftShardReward = data.DefeatRiftShardReward,
                Killer = killer,
            });

            f.SystemDisable<GameplaySystemGroup>();

            FPVector3 position = f.Unsafe.TryGetPointer<Transform3D>(boss, out var transform) ? transform->Position : FPVector3.Zero;
            f.Events.BossDefeated(boss, killer, position, duration, data.DefeatBurstTime);

            Log.Debug($"[Boss] {boss} defeated by {killer} - {duration}s defeat sequence, gameplay paused");
        }

        // Every other enemy pops (EnemyExploded, the same death VFX a Filler gets - no drops, no kill
        // credit), and every projectile plus every enemy-owned ground hazard is removed so nothing
        // hangs frozen in the air behind the cinematic. Collected first, destroyed after, so no
        // filter is mutated while it's being walked.
        public static void ClearArena(Frame f, EntityRef boss)
        {
            var toDestroy = new List<EntityRef>();

            var enemies = f.Filter<Enemy>();
            while (enemies.NextUnsafe(out EntityRef entity, out Enemy* enemy))
            {
                if (entity == boss)
                    continue;

                if (enemy->Phase != EnemyActionPhase.Dead)
                {
                    DamageUtility.FireEnemyExploded(f, entity, enemy->EnemyData);
                }

                toDestroy.Add(entity);
            }

            var projectiles = f.Filter<Projectile>();
            while (projectiles.NextUnsafe(out EntityRef entity, out Projectile* _))
            {
                toDestroy.Add(entity);
            }

            var areas = f.Filter<AreaOwner>();
            while (areas.NextUnsafe(out EntityRef entity, out AreaOwner* areaOwner))
            {
                if (f.Exists(areaOwner->Owner) == false || f.Has<Enemy>(areaOwner->Owner) == true)
                {
                    toDestroy.Add(entity);
                }
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (f.Exists(toDestroy[i]) == true)
                {
                    f.Destroy(toDestroy[i]);
                }
            }

            Log.Debug($"[Boss] defeat sequence cleared the arena ({toDestroy.Count} entities)");
        }

        // Removes every uncollected Rift Shard pickup and returns their summed base value (granted by the caller).
        public static FP CollectLooseRiftShards(Frame f)
        {
            FP total = FP._0;
            var toDestroy = new List<EntityRef>();

            var orbs = f.Filter<CurrencyOrb>();
            while (orbs.NextUnsafe(out EntityRef entity, out CurrencyOrb* orb))
            {
                if (orb->Type != CurrencyOrbType.RiftShard)
                    continue;

                total += orb->Value;
                toDestroy.Add(entity);
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                f.Destroy(toDestroy[i]);
            }

            return total;
        }

        private static bool IsAnotherBossAlive(Frame f, EntityRef boss)
        {
            var bosses = f.Filter<BossRuntimeState, Enemy>();

            while (bosses.NextUnsafe(out EntityRef entity, out BossRuntimeState* _, out Enemy* enemy))
            {
                if (entity != boss && enemy->Phase != EnemyActionPhase.Dead)
                    return true;
            }

            return false;
        }
    }
}
