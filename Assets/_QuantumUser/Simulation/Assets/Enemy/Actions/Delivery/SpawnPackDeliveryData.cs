namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Instantly spawns a fixed, ordered composition of real AI enemies scattered around an anchor -
    // e.g. Scrapjaw's Call the Pack. Deliberately NOT built on ScatterDeliveryData +
    // SpawnEntityEffectData: SpawnEntityEffectData funnels through SpawnedEntitySpawner.Spawn, which
    // unconditionally stamps DestroyAfterTime on whatever it spawns - fine for a bomb/decoy, wrong
    // for a real AI combatant, which would then despawn on a timer instead of acting like a normal
    // enemy. Mirrors GroupSpawnerUtility.SpawnMember's own create -> seed sequence directly instead,
    // deliberately WITHOUT adding EnemyLifecycle - these adds are boss-summoned, not a Director
    // purchase, so they don't count toward CombatDirectorUtility's pressure/alive-cap accounting and
    // won't be auto-retired by EnemyLifecycleSystem's Irrelevant timeout (confirmed safe: nothing
    // else in the codebase hard-requires EnemyLifecycle - only EnemyLifecycleSystem's own Filter and
    // CombatDirectorUtility's pressure math read that component, both simply skip entities without
    // it). Also skips GroupSpawnerUtility's own formation validation/ring-anchor search entirely -
    // this is a boss-triggered burst, not the Director's transactional wave spawn, so a simple
    // ground-height snap per point (no clearance overlap check) is enough. Always instant (Begin()
    // returns true), same as ScatterDeliveryData.
    public unsafe class SpawnPackDeliveryData : EnemyDeliveryData
    {
        // Exact, ordered composition - e.g. [Filler,Filler,Filler,Filler,Shooter,Shooter]. Not
        // weighted/random: a boss "calling the pack" wants a specific, readable roster every time,
        // not an approximation of one.
        public List<AssetRef<EnemyDataAsset>> Composition = new();

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.RuntimeConfig.DirectorConfig.Id.IsValid == false)
            {
                Log.Error("[Enemy] SpawnPackDeliveryData fired but RuntimeConfig.DirectorConfig isn't assigned - nothing spawned");
                return true;
            }

            DirectorConfig directorConfig = f.FindAsset(f.RuntimeConfig.DirectorConfig);
            FPVector3 anchor = action.Origin == EnemyActionOrigin.Self ? filter.Transform3D->Position : filter.Enemy->SkillTargetPosition;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            for (int i = 0; i < Composition.Count; i++)
            {
                if (Composition[i].IsValid == false)
                    continue;

                FPVector3 point = RandomizeAroundAnchor(f, anchor);

                FP groundY = EnemyMovementUtility.TryFindGroundHeight(f, point, groundLayerMask, out FP foundGroundY)
                    ? foundGroundY
                    : point.Y;

                SpawnMember(f, directorConfig, new FPVector3(point.X, groundY, point.Z), Composition[i], filter.Enemy->Faction);
            }

            return true;
        }

        // Mirrors GroupSpawnerUtility.SpawnMember's exact create -> seed sequence, minus the
        // EnemyLifecycle add - see class comment.
        private static void SpawnMember(Frame f, DirectorConfig directorConfig, FPVector3 position, AssetRef<EnemyDataAsset> enemyDataRef, EnemyFaction faction)
        {
            EntityRef entity = f.Create(directorConfig.EnemyPrototype);

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
            {
                Log.Error("[Enemy] DirectorConfig.EnemyPrototype has no Enemy component - destroying spawned pack member");
                f.Destroy(entity);
                return;
            }

            enemy->EnemyData = enemyDataRef;
            enemy->Faction = faction;
            f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;

            EnemyDataAsset data = f.FindAsset(enemyDataRef);
            EnemySystem.SeedFromEnemyData(f, entity, data);

            Log.Debug($"[Enemy] SpawnPackDeliveryData spawned {entity} ({data?.name ?? "NULL EnemyDataAsset"}) at {position}");
        }
    }
}
