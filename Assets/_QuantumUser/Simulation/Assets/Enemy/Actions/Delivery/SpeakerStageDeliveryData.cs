namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Riff Riot's "Som no Talo": instantly drops TWO stationary, destructible speaker cabinets (real
    // enemies, SpeakerEnemy) at valid, reachable, well-separated spots around the boss, then hands the
    // rest of the sequence to BossSpeakerStageSystem via the BossSpeakerStage component it adds to the
    // boss. Begin() returns true - the summon itself does NOT hold the boss: it goes straight back to
    // normal action selection (e.g. Power Chord) while the speakers play, which is why the emission
    // cadence lives in a system instead of a multi-tick Tick() here.
    //
    // The speakers never pick their own attack (their BasicAction should be authored with EngageRange
    // 0, so EnemyDecisionUtility never selects it) - BossSpeakerStageSystem forces it on one speaker at
    // a time, alternating, through the speaker's own normal Preparation -> Telegraph -> Begin pipeline,
    // so the windup visuals ARE the per-emission warning.
    //
    // This asset doubles as the sequence's tuning (BossSpeakerStage.Config points back at it).
    public unsafe class SpeakerStageDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<EnemyDataAsset> SpeakerEnemy;

        // Flat distance from the boss each speaker lands at - the search tries Max first, then
        // steps inward to Min.
        public FP SpawnDistanceMin = 4;
        public FP SpawnDistanceMax = 7;

        // The two speakers must end up at least this far apart (flat), so their rings never stack
        // into one wall.
        public FP MinSeparation = 7;

        // Max |floor height| difference from the boss's own floor - keeps both speakers on the arena
        // level the players are fighting on (reachable without jumping).
        public FP MaxHeightDifference = FP._1;

        // No speaker within this flat distance of any player - never drops one on top of a hero.
        public FP MinPlayerDistance = 2;

        [UnityEngine.Header("Sequence")]
        // Hard cap on the whole sequence. On expiry the remaining speakers are popped and removed with
        // NO exposure - the exposure is the reward for destroying both.
        public FP MaxDuration = 16;

        // Delay before the first emission, then the time between emissions (the two speakers
        // alternate, so each one fires every 2 x EmitInterval while both live). Divided by the
        // boss's current phase RecoveryMultiplier, so a faster phase tightens the cadence while each
        // speaker's own windup (the warning) stays the same length.
        public FP FirstEmitDelay = FP._1;
        public FP EmitInterval = FP.FromString("2.2");

        // Forced on the boss (via BossSystem.TryForceAction - deferred if it's mid-delivery, e.g.
        // airborne) the moment both speakers are destroyed. Must be listed in the boss's SkillActions.
        public AssetRef<EnemyActionData> ExposedAction;

        // Boss actions that may NOT be selected while the speakers are up (e.g. the leap - no jump
        // during the rings in this first version). See EnemyDecisionUtility.TrySelectAction.
        public List<AssetRef<EnemyActionData>> BlockedActionsWhileActive = new();

        // Optional "closer": when the sequence ends by expiry, this action's cooldown is cleared so the
        // boss picks it soon after - it still runs its full normal windup/telegraph.
        public AssetRef<EnemyActionData> ClosingAction;

        public override bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // One pair per boss - never stacks a second pair on top of a running sequence.
            if (f.Has<BossSpeakerStage>(filter.Entity) == true)
                return false;

            return TryFindSpawnPoints(f, filter.Entity, filter.Transform3D->Position, target, out _, out _);
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (f.Has<BossSpeakerStage>(filter.Entity) == true)
                return true;

            if (SpeakerEnemy.IsValid == false || f.GameConfig.DirectorConfig.Id.IsValid == false)
            {
                Log.Error("[Boss] SpeakerStageDeliveryData has no SpeakerEnemy, or OverallGameConfig.DirectorConfig is unassigned - nothing spawned");
                return true;
            }

            // Re-resolved here (the world may have changed during the windup); bail cleanly if the
            // spots CanBegin approved are no longer usable.
            if (TryFindSpawnPoints(f, filter.Entity, filter.Transform3D->Position, target, out FPVector3 a, out FPVector3 b) == false)
            {
                Log.Warn($"[Boss] {filter.Entity} lost its speaker spawn points during the windup - Som no Talo skipped");
                return true;
            }

            EntityRef speakerA = SpawnSpeaker(f, filter.Entity, a);
            EntityRef speakerB = SpawnSpeaker(f, filter.Entity, b);

            f.Add(filter.Entity, new BossSpeakerStage
            {
                Config = new AssetRef<SpeakerStageDeliveryData>(Guid),
                TimeRemaining = MaxDuration,
                EmitTimer = FirstEmitDelay,
            });

            BossSpeakerStage* stage = f.Unsafe.GetPointer<BossSpeakerStage>(filter.Entity);
            stage->Speakers[0] = speakerA;
            stage->Speakers[1] = speakerB;

            Log.Debug($"[Boss] {filter.Entity} Som no Talo - speakers {speakerA} at {a}, {speakerB} at {b}");
            return true;
        }

        private EntityRef SpawnSpeaker(Frame f, EntityRef boss, FPVector3 position)
        {
            DirectorConfig directorConfig = f.FindAsset(f.GameConfig.DirectorConfig);
            EntityRef entity = f.Create(directorConfig.EnemyPrototype);

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
            {
                Log.Error("[Boss] DirectorConfig.EnemyPrototype has no Enemy component - destroying speaker");
                f.Destroy(entity);
                return EntityRef.None;
            }

            // Same create -> seed sequence as SpawnPackDeliveryData.SpawnMember, without EnemyLifecycle
            // (boss-summoned, not a Director purchase).
            enemy->EnemyData = SpeakerEnemy;
            f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;
            EnemySystem.SeedFromEnemyData(f, entity, f.FindAsset(SpeakerEnemy));

            f.Add(entity, new BossSpeaker { Owner = boss });
            f.Add(entity, new BossSummon { Boss = boss });
            f.Events.BossSpeakerSpawned(boss, entity, position);
            return entity;
        }

        // Offsets tried around the boss->target axis, best first: pairs mirrored on both sides of the
        // fight (the players then stand between the speakers rather than behind one of them).
        private static readonly int[] SideAngles = { 90, 60, 120, 40, 140 };
        private const int DistanceSteps = 3;

        // Deterministic structured search (no RNG needed - the result only depends on sim state).
        // Each candidate must have ground at a reachable height, an unobstructed straight line from
        // the boss (same arena, not behind a wall/inside geometry) and no player standing on it.
        private bool TryFindSpawnPoints(Frame f, EntityRef boss, FPVector3 bossPosition, EntityRef target, out FPVector3 a, out FPVector3 b)
        {
            a = default;
            b = default;

            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, bossPosition, groundMask, out FP bossGroundY, boss) == false)
                return false;

            FP baseAngle = FP._0;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                FPVector3 toTarget = targetPosition - bossPosition;

                if (toTarget.X != FP._0 || toTarget.Z != FP._0)
                    baseAngle = FPMath.Atan2(toTarget.X, toTarget.Z) * FP.Rad2Deg;
            }

            for (int d = 0; d < DistanceSteps; d++)
            {
                FP distance = SpawnDistanceMax - (SpawnDistanceMax - SpawnDistanceMin) * d / (DistanceSteps - 1);

                for (int i = 0; i < SideAngles.Length; i++)
                {
                    if (TryResolvePoint(f, bossPosition, bossGroundY, baseAngle + SideAngles[i], distance, groundMask, out FPVector3 left) == false)
                        continue;

                    if (TryResolvePoint(f, bossPosition, bossGroundY, baseAngle - SideAngles[i], distance, groundMask, out FPVector3 right) == false)
                        continue;

                    if (EnemyMovementUtility.FlatSqrDistance(left, right) < MinSeparation * MinSeparation)
                        continue;

                    a = left;
                    b = right;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolvePoint(Frame f, FPVector3 bossPosition, FP bossGroundY, FP angle, FP distance, int groundMask, out FPVector3 point)
        {
            FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;
            point = bossPosition + direction * distance;

            if (EnemyMovementUtility.TryFindGroundHeight(f, point, groundMask, out FP groundY) == false)
                return false;

            if (FPMath.Abs(groundY - bossGroundY) > MaxHeightDifference)
                return false;

            // Straight line at the boss's own body height - a wall or a cliff face between the boss
            // and the spot means it's in another room / up on a ledge.
            if (EnemyMovementUtility.IsBlockedByWall(f, bossPosition, direction, distance, groundMask) == true)
                return false;

            point.Y = groundY;

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int playerCount = EnemyMovementUtility.FindPlayersInRadius(f, point, MinPlayerDistance, players);
            return playerCount == 0;
        }
    }
}
