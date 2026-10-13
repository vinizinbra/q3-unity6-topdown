namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Keeps each TrainingDummySpawner's dummy alive - see TrainingZone.qtn. Inside GameplaySystemGroup,
    // so a tutorial/level-up pause also freezes the respawn timer. Waits for LevelGenerated so the dummy
    // isn't created before the hand-placed chunk floor it stands on is part of the world.
    [Preserve]
    public unsafe class TrainingDummySpawnerSystem : SystemMainThreadFilter<TrainingDummySpawnerSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public TrainingDummySpawner* Spawner;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.Global->LevelGenerated == false)
                return;

            TrainingDummySpawner* spawner = filter.Spawner;

            if (IsAlive(f, spawner->Current))
                return;

            if (spawner->Current != default)
            {
                // Just died/despawned - start the respawn countdown.
                spawner->Current = default;
                spawner->RespawnTimer = spawner->RespawnDelay;
            }

            spawner->RespawnTimer -= f.DeltaTime;

            if (spawner->RespawnTimer > FP._0)
                return;

            spawner->Current = Spawn(f, filter.Entity, spawner, filter.Transform3D->Position);
        }

        // A lingering corpse (Enemy.Phase == Dead, kept for DeathLingerTime) already counts as gone, so
        // the respawn timer starts on the kill rather than after the corpse fades.
        private static bool IsAlive(Frame f, EntityRef dummy)
        {
            if (dummy == default || f.Exists(dummy) == false)
                return false;

            return f.Unsafe.TryGetPointer<Enemy>(dummy, out var enemy) == false || enemy->Phase != EnemyActionPhase.Dead;
        }

        private static EntityRef Spawn(Frame f, EntityRef spawnerEntity, TrainingDummySpawner* spawner, FPVector3 position)
        {
            if (spawner->Prototype.IsValid == false || spawner->EnemyData.IsValid == false)
                return default;

            EntityRef entity = f.Create(spawner->Prototype);

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
            {
                f.Destroy(entity);
                return default;
            }

            enemy->EnemyData = spawner->EnemyData;
            f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;

            EnemySystem.SeedFromEnemyData(f, entity, f.FindAsset(spawner->EnemyData));
            // Deliberately no EnemyLifecycle (unlike GroupSpawnerUtility.SpawnMember) - that's Director
            // bookkeeping, and EnemyLifecycleSystem would retire a dummy the moment the player is far
            // away (e.g. still at the start of the course), destroying it every few seconds.
            f.Add(entity, new TrainingDummy { Spawner = spawnerEntity });

            return entity;
        }
    }
}
