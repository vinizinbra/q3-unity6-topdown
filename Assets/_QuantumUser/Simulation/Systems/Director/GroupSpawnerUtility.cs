namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Domain 3 (Group Spawner) - the only thing this decides is WHERE a Director-selected group
    // can spawn safely; it never chooses which group, when, or whether it's affordable (that's
    // entirely CombatDirectorUtility's job, one layer up). Transactional per the design doc's
    // "Budget Transaction" section: every member position is found and validated first
    // (TryValidateFormation), and entities are only ever created (CreateGroup) after the WHOLE
    // formation is confirmed to fit - a partially-valid formation creates nothing and the caller
    // keeps its budget, same as a fully-invalid one.
    public static unsafe class GroupSpawnerUtility
    {
        // predictedCombatCenter is CombatDirectorUtility's own "moving combat bubble" - this
        // function only searches for an anchor near it and validates a formation there, it doesn't
        // know or care how that point was computed. isMajorGroup additionally requires the anchor's
        // own chunk to be the nearest player's chunk or directly connected to it (see
        // ChunkConnectivityUtility) - Elite+ groups anchor at the party's global centroid, a virtual
        // point that can otherwise land in a chunk that's merely close in world-space, not actually
        // reachable without a detour.
        public static bool TrySpawnGroup(Frame f, EnemyGroupConfig group, AssetRef<EnemyGroupConfig> groupRef, FPVector3 predictedCombatCenter, bool isMajorGroup, DirectorConfig directorConfig, out int spawnedCount)
        {
            spawnedCount = 0;

            if (directorConfig.EnemyPrototype.Id.IsValid == false)
            {
                Log.Error("[Spawner] DirectorConfig.EnemyPrototype not assigned - cannot spawn any group");
                return false;
            }

            int memberCount = group.ComputeMemberCount();

            if (memberCount == 0)
            {
                Log.Error($"[Spawner] {group.name} has no Members/Quantity authored - nothing to spawn");
                return false;
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            int noGroundCount = 0;
            int disconnectedChunkCount = 0;
            int invalidFormationCount = 0;

            // Each attempt is a fresh, independent anchor - no per-member retry/relaxation within
            // an attempt (see DirectorConfig.MaxGroupSpawnAttempts's own comment). A formation
            // that almost fits at one anchor is simply discarded, not nudged.
            for (int attempt = 0; attempt < directorConfig.MaxGroupSpawnAttempts; attempt++)
            {
                FPVector3 candidateAnchor = EnemyMovementUtility.RandomPositionInRing(f, predictedCombatCenter, directorConfig.SpawnRingRadiusMin, directorConfig.SpawnRingRadiusMax);

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidateAnchor, groundLayerMask, out FP anchorGroundY) == false)
                {
                    noGroundCount++;
                    continue; // no floor under this ring position at all - try another anchor
                }

                FPVector3 anchor = new FPVector3(candidateAnchor.X, anchorGroundY, candidateAnchor.Z);

                if (isMajorGroup && ChunkConnectivityUtility.IsConnectedToNearestPlayer(f, anchor) == false)
                {
                    disconnectedChunkCount++;
                    continue; // close in world-space, but not reachable from the nearest player's own chunk
                }

                if (TryValidateFormation(f, group, memberCount, anchor, anchorGroundY, groundLayerMask, directorConfig, out FPVector3[] memberPositions, out AssetRef<EnemyDataAsset>[] memberData, out EnemyFaction[] memberFaction) == false)
                {
                    invalidFormationCount++;
                    continue; // one or more members didn't fit here - discard this whole anchor
                }

                Log.Error($"[Spawner] {group.name} anchor found at attempt {attempt} ({anchor}) - spawning {memberCount} member(s)");
                CreateGroup(f, groupRef, directorConfig, memberPositions, memberData, memberFaction);
                spawnedCount = memberCount;
                return true;
            }

            Log.Error($"[Spawner] {group.name} found no valid anchor after {directorConfig.MaxGroupSpawnAttempts} attempts near {predictedCombatCenter} - {noGroundCount} had no ground, {disconnectedChunkCount} landed in a disconnected chunk, {invalidFormationCount} had an invalid formation (see [Spawner] member-rejection logs above for why)");
            return false;
        }

        // AllowedEnemies' counterpart to TrySpawnGroup - same anchor-attempt/ground/chunk-
        // connectivity loop, but with no formation to solve (a single slot at the anchor itself)
        // and no group asset to own the spawn (SpawnMember is called with a default/invalid
        // groupRef, so EnemyLifecycle.SourceGroup reads as "not from any group" - CountAliveForGroup
        // is keyed by a specific AssetRef<EnemyGroupConfig>, so a default value there is inert).
        // Reuses TryValidateMember for the exact same height-rule/forbidden-chunk/clearance checks
        // a group member gets, just against one point instead of an offset formation.
        public static bool TrySpawnEnemy(Frame f, EnemySpawnEntry entry, FPVector3 predictedCombatCenter, bool isMajor, DirectorConfig directorConfig)
        {
            if (directorConfig.EnemyPrototype.Id.IsValid == false)
            {
                Log.Error("[Spawner] DirectorConfig.EnemyPrototype not assigned - cannot spawn a direct enemy");
                return false;
            }

            if (entry.EnemyData.Id.IsValid == false)
            {
                Log.Error("[Spawner] EnemySpawnEntry has no EnemyData assigned - nothing to spawn");
                return false;
            }

            EnemyDataAsset data = f.FindAsset(entry.EnemyData);

            if (data == null || data.Economy.SpawnProfile.Id.IsValid == false)
            {
                Log.Error($"[Spawner] {entry.EnemyData} has no SpawnProfile assigned - cannot place it directly");
                return false;
            }

            EnemySpawnProfile profile = f.FindAsset(data.Economy.SpawnProfile);
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            int noGroundCount = 0;
            int disconnectedChunkCount = 0;
            int invalidCount = 0;

            for (int attempt = 0; attempt < directorConfig.MaxGroupSpawnAttempts; attempt++)
            {
                FPVector3 candidateAnchor = EnemyMovementUtility.RandomPositionInRing(f, predictedCombatCenter, directorConfig.SpawnRingRadiusMin, directorConfig.SpawnRingRadiusMax);

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidateAnchor, groundLayerMask, out FP anchorGroundY) == false)
                {
                    noGroundCount++;
                    continue;
                }

                FPVector3 anchor = new FPVector3(candidateAnchor.X, anchorGroundY, candidateAnchor.Z);

                if (isMajor && ChunkConnectivityUtility.IsConnectedToNearestPlayer(f, anchor) == false)
                {
                    disconnectedChunkCount++;
                    continue;
                }

                if (TryValidateMember(f, data.name, 0, anchor, anchorGroundY, profile, groundLayerMask, directorConfig, out FPVector3 groundedPosition) == false)
                {
                    invalidCount++;
                    continue;
                }

                Log.Error($"[Spawner] {data.name} direct spawn anchor found at attempt {attempt} ({anchor})");
                SpawnMember(f, default, directorConfig, groundedPosition, entry.EnemyData, entry.Faction);
                return true;
            }

            Log.Error($"[Spawner] {data.name} direct spawn found no valid anchor after {directorConfig.MaxGroupSpawnAttempts} attempts near {predictedCombatCenter} - {noGroundCount} had no ground, {disconnectedChunkCount} landed in a disconnected chunk, {invalidCount} failed placement");
            return false;
        }

        // Flattens every Member's Quantity into individual formation slots (slot 0..memberCount-1,
        // continuous across all Members, not restarted per Member) so GroupFormationUtility sees
        // one coherent shape across the whole group rather than one shape per enemy type.
        private static bool TryValidateFormation(Frame f, EnemyGroupConfig group, int memberCount, FPVector3 anchor, FP anchorGroundY, int groundLayerMask, DirectorConfig directorConfig, out FPVector3[] memberPositions, out AssetRef<EnemyDataAsset>[] memberData, out EnemyFaction[] memberFaction)
        {
            memberPositions = new FPVector3[memberCount];
            memberData = new AssetRef<EnemyDataAsset>[memberCount];
            memberFaction = new EnemyFaction[memberCount];

            // Decided once per attempt, shared by every member - the ring only picked WHERE
            // (the anchor point); this independent roll decides the formation's orientation, so
            // the same authored pattern reads differently spawn to spawn without any extra
            // authoring (same idiom the previous fixed-offset SpawnGroup already used).
            FP facing = f.RNG->Next(0, 360);
            FPQuaternion rotation = FPQuaternion.Euler(FP._0, facing, FP._0);

            int slot = 0;

            foreach (GroupMemberEntry member in group.Members)
            {
                if (member.EnemyData.Id.IsValid == false)
                {
                    Log.Error($"[Spawner] {group.name} has a Member with no EnemyData assigned - rejecting the whole group");
                    return false;
                }

                EnemyDataAsset data = f.FindAsset(member.EnemyData);

                if (data.Economy.SpawnProfile.Id.IsValid == false)
                {
                    Log.Error($"[Spawner] {data.name} has no SpawnProfile assigned - rejecting {group.name}");
                    return false;
                }

                EnemySpawnProfile profile = f.FindAsset(data.Economy.SpawnProfile);

                for (int copy = 0; copy < member.Quantity; copy++)
                {
                    FPVector2 localOffset = GroupFormationUtility.ComputeLocalOffset(f, group.SpawnPattern, slot, memberCount, group.FormationRadius);
                    FPVector3 worldOffset = rotation * new FPVector3(localOffset.X, FP._0, localOffset.Y);
                    FPVector3 horizontalCandidate = anchor + worldOffset;

                    if (TryValidateMember(f, data.name, slot, horizontalCandidate, anchorGroundY, profile, groundLayerMask, directorConfig, out FPVector3 groundedPosition) == false)
                        return false;

                    memberPositions[slot] = groundedPosition;
                    memberData[slot] = member.EnemyData;
                    memberFaction[slot] = member.Faction;
                    slot++;
                }
            }

            return true;
        }

        // Ground detection + height rule + chunk-type + clearance, in that order (cheapest/
        // most-likely-to-reject check first, physics overlap query last) - a single failed member
        // fails the whole formation, see TryValidateFormation.
        private static bool TryValidateMember(Frame f, string dataName, int slot, FPVector3 horizontalCandidate, FP anchorGroundY, EnemySpawnProfile profile, int groundLayerMask, DirectorConfig directorConfig, out FPVector3 groundedPosition)
        {
            if (EnemyMovementUtility.TryFindGroundHeight(f, horizontalCandidate, groundLayerMask, out FP groundY) == false)
            {
                groundedPosition = default;
                Log.Error($"[Spawner] {dataName} slot {slot} rejected - no ground under {horizontalCandidate}");
                return false;
            }

            groundedPosition = new FPVector3(horizontalCandidate.X, groundY, horizontalCandidate.Z);

            if (ValidateHeightRule(profile, groundY, anchorGroundY) == false)
            {
                Log.Error($"[Spawner] {dataName} slot {slot} rejected - height difference {groundY - anchorGroundY} outside [{profile.MinimumHeightDifference}, {profile.MaximumHeightDifference}] for {profile.SpawnCategory}");
                return false;
            }

            if (IsInForbiddenChunk(f, groundedPosition, directorConfig, out ChunkType forbiddenType))
            {
                Log.Error($"[Spawner] {dataName} slot {slot} rejected - {groundedPosition} falls inside a {forbiddenType} chunk (DirectorConfig.ForbiddenSpawnChunkTypes)");
                return false;
            }

            if (HasClearance(f, groundedPosition, profile) == false)
            {
                Log.Error($"[Spawner] {dataName} slot {slot} rejected - no clearance at {groundedPosition} (blocked by player/enemy/obstacle)");
                return false;
            }

            return true;
        }

        // Rejects a candidate landing inside a chunk whose Type is listed in
        // DirectorConfig.ForbiddenSpawnChunkTypes (e.g. Traversal, to keep connector corridors
        // clear) - reuses EnemyPathfindingUtility's own point-in-chunk lookup rather than
        // re-deriving chunk AABB math here. Never rejects if the list is empty/unassigned, or if
        // the candidate doesn't land inside any Chunk entity at all (e.g. FillInnerGaps filler
        // geometry has no Chunk component) - only an explicit Type match rejects.
        private static bool IsInForbiddenChunk(Frame f, FPVector3 position, DirectorConfig directorConfig, out ChunkType chunkType)
        {
            chunkType = default;

            if (directorConfig.ForbiddenSpawnChunkTypes == null || directorConfig.ForbiddenSpawnChunkTypes.Length == 0)
            {
                return false;
            }

            if (EnemyPathfindingUtility.TryFindContainingChunk(f, position, out EntityRef chunkEntity) == false)
            {
                return false;
            }

            chunkType = f.Unsafe.GetPointer<Chunk>(chunkEntity)->Type;

            foreach (ChunkType forbidden in directorConfig.ForbiddenSpawnChunkTypes)
            {
                if (forbidden == chunkType)
                {
                    return true;
                }
            }

            return false;
        }

        // Only GroundMelee/GroundRanged are height-restricted today - Flying/HighGroundRanged/Boss
        // skip this entirely (see EnemySpawnProfile's own comment on why those two categories are
        // placeholder-only until a later milestone).
        private static bool ValidateHeightRule(EnemySpawnProfile profile, FP groundY, FP anchorGroundY)
        {
            if (profile.SpawnCategory != EnemySpawnCategory.GroundMelee && profile.SpawnCategory != EnemySpawnCategory.GroundRanged)
                return true;

            FP heightDifference = groundY - anchorGroundY;
            return heightDifference >= profile.MinimumHeightDifference && heightDifference <= profile.MaximumHeightDifference;
        }

        // Vertical capsule overlap centered on the candidate ground position - rejects a spot that
        // overlaps a player, another enemy, or blocking level geometry (Obstacle layer). Not
        // checked against the Ground layer itself - TryFindGroundHeight already confirmed a floor
        // is there; this only asks whether the space directly above it is free.
        private static bool HasClearance(Frame f, FPVector3 groundedPosition, EnemySpawnProfile profile)
        {
            int layerMask = EnemyMovementUtility.GetPlayerLayerMask(f) | EnemyMovementUtility.GetEnemyLayerMask(f) | EnemyMovementUtility.GetObstacleLayerMask(f);

            // Shape3D.CreateCapsule(radius, extent) - extent is the half-height of the straight
            // cylindrical section only (excludes the two rounded end caps), same convention
            // KCC.Physics.cs already uses: extent = totalHeight / 2 - radius, clamped so a
            // ClearanceHeight authored smaller than 2x ClearanceRadius still produces a valid
            // (sphere-like) capsule instead of a negative extent.
            FP radius = profile.ClearanceRadius;
            FP extent = FPMath.Max(FP._0, profile.ClearanceHeight * FP._0_50 - radius);
            Shape3D capsule = Shape3D.CreateCapsule(radius, extent);
            FPVector3 origin = groundedPosition + FPVector3.Up * profile.ClearanceHeight * FP._0_50;

            var hits = f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, capsule, layerMask, QueryOptions.HitAll);
            return hits.Count == 0;
        }

        // Called only once TrySpawnGroup already confirmed every member position is valid - never
        // partially applied.
        private static void CreateGroup(Frame f, AssetRef<EnemyGroupConfig> groupRef, DirectorConfig directorConfig, FPVector3[] memberPositions, AssetRef<EnemyDataAsset>[] memberData, EnemyFaction[] memberFaction)
        {
            for (int i = 0; i < memberPositions.Length; i++)
            {
                SpawnMember(f, groupRef, directorConfig, memberPositions[i], memberData[i], memberFaction[i]);
            }
        }

        // Every Director purchase is created off one shared generic prototype
        // (DirectorConfig.EnemyPrototype, e.g. BasicEnemy) rather than a prototype baked per enemy
        // type - see DirectorConfig.EnemyPrototype's own comment for why. Enemy->EnemyData is set
        // here, AFTER f.Create, which means EnemySystem.OnEntityPrototypeMaterialized already ran
        // this same tick against an empty EnemyData and did nothing (see that signal's own
        // comment) - EnemySystem.SeedFromEnemyData re-runs the same Health/Shield/Radius seeding
        // manually right afterward, so the result is identical to an entity that had EnemyData
        // baked in from the start.
        private static void SpawnMember(Frame f, AssetRef<EnemyGroupConfig> groupRef, DirectorConfig directorConfig, FPVector3 position, AssetRef<EnemyDataAsset> enemyDataRef, EnemyFaction faction)
        {
            EntityRef entity = f.Create(directorConfig.EnemyPrototype);

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
            {
                Log.Error("[Spawner] DirectorConfig.EnemyPrototype has no Enemy component - destroying spawned entity");
                f.Destroy(entity);
                return;
            }

            enemy->EnemyData = enemyDataRef;
            enemy->Faction = faction;
            f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;

            EnemyDataAsset data = f.FindAsset(enemyDataRef);
            EnemySystem.SeedFromEnemyData(f, entity, data);

            if (f.Add(entity, out EnemyLifecycle* lifecycle) == AddResult.ComponentAdded)
            {
                lifecycle->State = EnemyLifecycleState.Active;
                lifecycle->SourceGroup = groupRef;
            }

            Log.Error($"[Spawner] spawned {entity} ({data?.name ?? "NULL EnemyDataAsset"}) at {position}");
        }
    }
}
