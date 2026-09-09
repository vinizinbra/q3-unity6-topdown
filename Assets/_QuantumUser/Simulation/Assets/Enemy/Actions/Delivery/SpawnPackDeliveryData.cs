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
        // Exact, ordered composition - e.g. [Filler,Filler,Filler,Filler,Shooter,Shooter]. Every
        // FULL cycle through it spawns in this exact authored order (a boss "calling the pack" wants
        // a specific, readable roster every time) - only the trailing partial cycle (see MaxEnemies*
        // below) picks randomly instead, since there's no "correct" authored order for half a roster.
        public List<AssetRef<EnemyDataAsset>> Composition = new();

        // Total members spawned by one Begin() call, scaled by live player count - same clamped-
        // [1,4]-then-switch idiom ScatterDeliveryData.ResolveCount already uses. This is a fill
        // target, not just a ceiling: Composition repeats as many FULL cycles as fit, then a final
        // PARTIAL cycle (drawn randomly, without replacement, from Composition via
        // WeightedDrawUtility) tops up to exactly MaxEnemies - e.g. a 4-entry Composition at
        // MaxEnemies=6 spawns the whole roster once (4) plus 2 more entries picked randomly out of
        // those same 4. Supersedes BossPhaseUtility.QuantityMultiplier for this delivery (a boss-
        // phase repeat count would always be capped/topped-up to MaxEnemies anyway, so authoring both
        // would be redundant) - other deliveries (Mortar, Scatter) still read QuantityMultiplier.
        public int MaxEnemiesP1 = 5;
        public int MaxEnemiesP2 = 8;
        public int MaxEnemiesP3 = 10;
        public int MaxEnemiesP4 = 12;

        private int ResolveMaxEnemies(Frame f)
        {
            int clamped = f.PlayerConnectedCount < 1 ? 1 : (f.PlayerConnectedCount > 4 ? 4 : f.PlayerConnectedCount);
            return clamped switch { 1 => MaxEnemiesP1, 2 => MaxEnemiesP2, 3 => MaxEnemiesP3, _ => MaxEnemiesP4 };
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.RuntimeConfig.DirectorConfig.Id.IsValid == false)
            {
                Log.Error("[Enemy] SpawnPackDeliveryData fired but RuntimeConfig.DirectorConfig isn't assigned - nothing spawned");
                return true;
            }

            List<AssetRef<EnemyDataAsset>> validComposition = new(Composition.Count);
            for (int i = 0; i < Composition.Count; i++)
            {
                if (Composition[i].IsValid)
                    validComposition.Add(Composition[i]);
            }

            if (validComposition.Count == 0)
                return true;

            DirectorConfig directorConfig = f.FindAsset(f.RuntimeConfig.DirectorConfig);
            FPVector3 anchor = action.Origin == EnemyActionOrigin.Self ? filter.Transform3D->Position : filter.Enemy->SkillTargetPosition;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            EnemyFaction faction = filter.Enemy->Faction;

            int maxEnemies = ResolveMaxEnemies(f);
            int fullCycles = maxEnemies / validComposition.Count;
            int remainder = maxEnemies % validComposition.Count;

            for (int cycle = 0; cycle < fullCycles; cycle++)
            {
                for (int i = 0; i < validComposition.Count; i++)
                    SpawnAtAnchor(f, directorConfig, anchor, groundLayerMask, validComposition[i], faction);
            }

            if (remainder > 0)
            {
                List<WeightedDrawUtility.Candidate<AssetRef<EnemyDataAsset>>> candidates = new(validComposition.Count);
                for (int i = 0; i < validComposition.Count; i++)
                    candidates.Add(new WeightedDrawUtility.Candidate<AssetRef<EnemyDataAsset>> { Value = validComposition[i], Weight = 1 });

                AssetRef<EnemyDataAsset>[] picked = WeightedDrawUtility.Draw(f, candidates, remainder);
                for (int i = 0; i < picked.Length; i++)
                    SpawnAtAnchor(f, directorConfig, anchor, groundLayerMask, picked[i], faction);
            }

            return true;
        }

        private void SpawnAtAnchor(Frame f, DirectorConfig directorConfig, FPVector3 anchor, int groundLayerMask, AssetRef<EnemyDataAsset> enemyDataRef, EnemyFaction faction)
        {
            FPVector3 point = RandomizeAroundAnchor(f, anchor);

            FP groundY = EnemyMovementUtility.TryFindGroundHeight(f, point, groundLayerMask, out FP foundGroundY)
                ? foundGroundY
                : point.Y;

            SpawnMember(f, directorConfig, new FPVector3(point.X, groundY, point.Z), enemyDataRef, faction);
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
