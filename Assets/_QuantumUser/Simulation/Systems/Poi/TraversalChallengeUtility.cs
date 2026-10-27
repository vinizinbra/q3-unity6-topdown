namespace Quantum
{
    using Photon.Deterministic;

    // Traversal Challenge's own interaction - unlike HealingShrine (immediate, one-shot) or
    // CursedRift (per-player session component), this is a WORLD-shared timed attempt: one press
    // starts it for everyone, any connected player can complete it, State alone (not PoiUsage) governs
    // re-triggerability. See TraversalChallenge.qtn/docs/traversal-challenge.md.
    public static unsafe class TraversalChallengeUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution and Busy already happened there via the sibling Interactable component).
        // AlreadyUsed covers BOTH "someone else is mid-attempt right now" (Active) and "already
        // solved" (Completed) - this deliberately does NOT return Busy, which is reserved for
        // PoiInteractionLockUtility's own generic "mid a different POI's Choice Window" check.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef challenge)
        {
            if (f.Unsafe.TryGetPointer<TraversalChallenge>(challenge, out var traversalChallenge) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, traversalChallenge->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (traversalChallenge->State != TraversalChallengeState.Idle)
                return ContextInteractionState.AlreadyUsed;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-resolves state fresh (never trusts ContextInteraction.State alone) -
        // required here specifically, since two players can both see Available cached from earlier
        // this same tick and both press in the same Update() pass; the first TryActivate flips State
        // to Active synchronously (Quantum ticks single-threaded), so the second's fresh re-read
        // correctly blocks.
        public static void TryActivate(Frame f, EntityRef player, EntityRef challenge)
        {
            if (ResolveInteractionState(f, player, challenge) != ContextInteractionState.Available)
                return;

            var traversalChallenge = f.Unsafe.GetPointer<TraversalChallenge>(challenge);

            // Chunk-relative, not activator-relative - resolve (and cache) the owning chunk here so
            // both this spawn and TraversalChallengeSystem's later checkpoint check share the exact
            // same anchor. Nearest chunk, not strict containment - same "Chunk seam gap pattern"
            // FallRespawnUtility.ResolveNearestChunkRespawnPosition already relies on for
            // Chunk.RespawnPoint, since a hand-placed prop can sit right at a chunk boundary seam.
            ResolveChunkAnchor(f, challenge, out EntityRef chunkEntity, out FPVector3 anchorPosition, out FPQuaternion anchorRotation);
            traversalChallenge->Chunk = chunkEntity;

            traversalChallenge->State = TraversalChallengeState.Active;
            traversalChallenge->RemainingTime = ResolveDuration(f, traversalChallenge);
            traversalChallenge->ActivatedBy = player;
            f.Global->ActiveTraversalChallengeCount++;

            var positions = traversalChallenge->PlatformPositions;
            var spawned = traversalChallenge->SpawnedPlatforms;
            int platformCount = traversalChallenge->PlatformCount < positions.Length ? traversalChallenge->PlatformCount : positions.Length;

            for (int i = 0; i < platformCount; i++)
            {
                EntityRef platform = f.Create(traversalChallenge->PlatformPrototype);
                f.Unsafe.GetPointer<Transform3D>(platform)->Position = anchorPosition + anchorRotation * positions[i];
                spawned[i] = platform;
            }

            SpawnReward(f, traversalChallenge, anchorPosition, anchorRotation);

            f.Events.TraversalChallengeActivated(challenge, player);

            Log.Debug($"[TraversalChallenge] {player} activated {challenge} - {platformCount} platform(s) + reward {traversalChallenge->SpawnedReward} spawned, {traversalChallenge->RemainingTime.AsFloat}s to reach the checkpoint");
        }

        // Called from TraversalChallengeSystem the tick a player reaches the checkpoint. Platforms
        // are deliberately left alone (stay solid forever) - a player still fighting elsewhere can
        // walk over later with no time pressure, only the crossing itself was ever timed.
        public static void Complete(Frame f, EntityRef challenge, TraversalChallenge* traversalChallenge)
        {
            traversalChallenge->State = TraversalChallengeState.Completed;
            Decrement(f);
            f.Events.TraversalChallengeCompleted(challenge);

            Log.Debug($"[TraversalChallenge] {challenge} completed");
        }

        // Called from TraversalChallengeSystem the tick RemainingTime runs out with nobody at the
        // checkpoint. Destroys every spawned platform and settles on Failed, NOT Idle - one attempt
        // total per run, same as Completed (confirmed with the user): a timed-out attempt isn't any
        // more retryable than a solved one is.
        public static void Fail(Frame f, EntityRef challenge, TraversalChallenge* traversalChallenge)
        {
            var spawned = traversalChallenge->SpawnedPlatforms;

            for (int i = 0; i < spawned.Length; i++)
            {
                if (spawned[i] != EntityRef.None && f.Exists(spawned[i]) == true)
                    f.Destroy(spawned[i]);

                spawned[i] = EntityRef.None;
            }

            // The reward only ever existed because of this attempt - a failed crossing takes it with
            // it. Guarded on Opened: a player who somehow reached the chest without touching the
            // checkpoint already earned it (its own DestroyAfterTime cleanup is underway).
            EntityRef reward = traversalChallenge->SpawnedReward;

            if (reward != EntityRef.None && f.Exists(reward) == true
                && (f.Unsafe.TryGetPointer<Chest>(reward, out var chest) == false || chest->Opened == false))
            {
                f.Destroy(reward);
            }

            traversalChallenge->SpawnedReward = EntityRef.None;

            traversalChallenge->State = TraversalChallengeState.Failed;
            Decrement(f);
            f.Events.TraversalChallengeFailed(challenge);

            Log.Debug($"[TraversalChallenge] {challenge} timed out - platforms + reward destroyed, permanently failed");
        }

        // Uniform deterministic roll (f.RNG) over the authored RewardPrototypes - same "pick one of N
        // chest kinds" the old baked ChunkSpawnConfig entry did with its equal-weight Prototypes
        // list. Rolled once, up front (TraversalChallengeSystem's first tick, or TryActivate as a
        // fallback), so the prompt can preview the exact reward before anyone activates - same
        // "roll early so the prompt is real" idea as TeamChallengeUtility.EnsureChallengeRolled.
        public static void EnsureRewardRolled(Frame f, TraversalChallenge* traversalChallenge)
        {
            if (traversalChallenge->RewardRolled == true)
                return;

            int count = ResolveRewardCount(traversalChallenge);
            traversalChallenge->RolledRewardIndex = (byte)(count > 0 ? f.RNG->Next(0, count) : 0);
            traversalChallenge->RewardRolled = true;
        }

        // The prototype that will spawn (or did) - invalid when nothing is authored or not yet
        // rolled. Read by TraversalChallengeView for the prompt's reward preview.
        public static AssetRef<EntityPrototype> GetRolledReward(TraversalChallenge* traversalChallenge)
        {
            int count = ResolveRewardCount(traversalChallenge);

            if (traversalChallenge->RewardRolled == false || traversalChallenge->RolledRewardIndex >= count)
                return default;

            return traversalChallenge->RewardPrototypes[traversalChallenge->RolledRewardIndex];
        }

        private static int ResolveRewardCount(TraversalChallenge* traversalChallenge)
        {
            int length = traversalChallenge->RewardPrototypes.Length;
            return traversalChallenge->RewardPrototypeCount < length ? traversalChallenge->RewardPrototypeCount : length;
        }

        // Called from TryActivate. Same placement as the chunk's own spawner (TalentGateSystem):
        // chunk position + chunk rotation * offset, then GroundOffsetUtility.Apply so the chest sits
        // on the floor exactly like the old baked one did.
        private static void SpawnReward(Frame f, TraversalChallenge* traversalChallenge, FPVector3 anchorPosition, FPQuaternion anchorRotation)
        {
            EnsureRewardRolled(f, traversalChallenge);
            AssetRef<EntityPrototype> prototype = GetRolledReward(traversalChallenge);

            if (prototype.IsValid == false)
                return;

            EntityRef reward = f.Create(prototype);
            f.Unsafe.GetPointer<Transform3D>(reward)->Position = anchorPosition + anchorRotation * traversalChallenge->RewardPosition;
            GroundOffsetUtility.Apply(f, reward);
            traversalChallenge->SpawnedReward = reward;
        }

        // True once there's nothing left to collect: no reward authored, the reward entity is gone,
        // or it's a Chest that's been opened. Read by TraversalChallengeSystem while Completed to
        // decide when the return pad appears.
        public static bool IsRewardCollected(Frame f, TraversalChallenge* traversalChallenge)
        {
            EntityRef reward = traversalChallenge->SpawnedReward;

            if (reward == EntityRef.None || f.Exists(reward) == false)
                return true;

            return f.Unsafe.TryGetPointer<Chest>(reward, out var chest) == true && chest->Opened == true;
        }

        // Called from TraversalChallengeSystem once Completed + IsRewardCollected. Both the pad and
        // its destination are ground-snapped (same top-down ground raycast RunPhaseUtility.
        // GroundCorrect uses for boss-arena teleports), so authored Y only has to be roughly right.
        public static void SpawnReturnTeleport(Frame f, EntityRef challenge, TraversalChallenge* traversalChallenge)
        {
            if (traversalChallenge->ReturnTeleportPrototype.IsValid == false)
                return;

            ResolveCachedAnchor(f, challenge, traversalChallenge, out FPVector3 anchorPosition, out FPQuaternion anchorRotation);
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            FPVector3 padPosition = SnapToGround(f, anchorPosition + anchorRotation * traversalChallenge->ReturnTeleportPosition, groundLayerMask);
            FPVector3 destination = SnapToGround(f, anchorPosition + anchorRotation * traversalChallenge->ReturnDestinationPosition, groundLayerMask);

            EntityRef pad = f.Create(traversalChallenge->ReturnTeleportPrototype);
            f.Unsafe.GetPointer<Transform3D>(pad)->Position = padPosition;

            if (f.Unsafe.TryGetPointer<TraversalReturnTeleport>(pad, out var teleport) == true)
            {
                teleport->Destination = destination;
                teleport->Challenge = challenge;
            }

            traversalChallenge->SpawnedReturnTeleport = pad;

            Log.Debug($"[TraversalChallenge] {challenge} reward collected - return pad {pad} spawned at {padPosition}, leads to {destination}");
        }

        private static FPVector3 SnapToGround(Frame f, FPVector3 position, int groundLayerMask)
        {
            if (EnemyMovementUtility.TryFindGroundHeight(f, position, groundLayerMask, out FP groundY) == true)
                return new FPVector3(position.X, groundY, position.Z);

            return position;
        }

        // Clamped at 0 as cheap insurance against a future double-decrement bug permanently freezing
        // SurvivalTime/spawning for the rest of the run.
        // The run's difficulty tier picks the timer from TraversalChallengeConfig; the activator's own
        // Duration is only the fallback when OverallGameConfig has no TraversalChallengeConfig assigned.
        private static FP ResolveDuration(Frame f, TraversalChallenge* traversalChallenge)
        {
            if (f.GameConfig.TraversalChallengeConfig.IsValid == false)
                return traversalChallenge->Duration;

            return f.FindAsset(f.GameConfig.TraversalChallengeConfig).GetDuration(f.Global->Difficulty.Tier);
        }

        private static void Decrement(Frame f)
        {
            if (f.Global->ActiveTraversalChallengeCount > 0)
                f.Global->ActiveTraversalChallengeCount--;
        }

        // Fresh nearest-chunk lookup, called once from TryActivate (see TraversalChallenge.Chunk's
        // own comment on why it's cached rather than re-resolved every tick). Falls back to this
        // entity's own Transform3D/identity rotation in the - practically unreachable outside a
        // level with zero Chunk entities - case TryFindNearestChunk finds nothing, same "always
        // return something usable" shape FallRespawnUtility.ResolveNearestChunkRespawnPosition
        // itself falls back to.
        private static void ResolveChunkAnchor(Frame f, EntityRef challenge, out EntityRef chunkEntity, out FPVector3 anchorPosition, out FPQuaternion anchorRotation)
        {
            Transform3D challengeTransform = f.Get<Transform3D>(challenge);

            if (FallRespawnUtility.TryFindNearestChunk(f, challengeTransform.Position, out chunkEntity) == true)
            {
                Transform3D chunkTransform = f.Get<Transform3D>(chunkEntity);
                anchorPosition = chunkTransform.Position;
                anchorRotation = chunkTransform.Rotation;
                return;
            }

            anchorPosition = challengeTransform.Position;
            anchorRotation = FPQuaternion.Identity;
        }

        // Reads the Chunk cached by TryActivate (see TraversalChallenge.Chunk) instead of re-scanning
        // every Chunk entity every tick - called from TraversalChallengeSystem while State == Active.
        // Falls back to this entity's own Transform3D/identity rotation if the cached ref is somehow
        // gone (a chunk is static level geometry that's never destroyed at runtime today, so this is
        // defensive, not an expected path).
        public static void ResolveCachedAnchor(Frame f, EntityRef challenge, TraversalChallenge* traversalChallenge, out FPVector3 anchorPosition, out FPQuaternion anchorRotation)
        {
            if (traversalChallenge->Chunk != EntityRef.None && f.Exists(traversalChallenge->Chunk) == true)
            {
                Transform3D chunkTransform = f.Get<Transform3D>(traversalChallenge->Chunk);
                anchorPosition = chunkTransform.Position;
                anchorRotation = chunkTransform.Rotation;
                return;
            }

            anchorPosition = f.Get<Transform3D>(challenge).Position;
            anchorRotation = FPQuaternion.Identity;
        }
    }
}
