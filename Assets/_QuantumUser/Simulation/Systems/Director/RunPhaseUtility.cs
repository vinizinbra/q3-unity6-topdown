namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // One-shot side effects for the Combat<->Breathing edge (see docs/run-phase.md) - called by
    // CombatDirectorSystem only on the tick the CURRENT SurvivalPhase's Kind actually changes
    // (Breathing is just another entry in SurvivalConfig.Phases, see SurvivalConfig.cs). Entering
    // Breathing has no side effect of its own here - enemies are deliberately left alone (no
    // force-clear) so SurvivalProgressionUtility.IsEncounterCleared's own Breathing hold has
    // something real to wait on; see CombatDirectorSystem.ApplyPhaseGameState's own comment.
    public static unsafe class RunPhaseUtility
    {
        // Section 31 "Situation A" of the design brief: a player still SelectingSacrifice (no cost
        // paid yet) when the Break ends has their interaction cancelled outright, no cost applied.
        // A player who already picked a sacrifice (SelectingMutation - cost already applied by
        // CursedRiftUtility.SelectSacrifice) is deliberately left alone and must finish -
        // CursedRiftSystem keeps processing their commands regardless of CurrentState, so this
        // sweep is the ONLY place Breathing's own end has any effect on an in-progress interaction.
        // Collected into a list first rather than removed mid-filter-iteration, same precaution
        // every other sweep in this file uses.
        public static void CancelUncommittedCursedRiftInteractions(Frame f)
        {
            List<EntityRef> toCancel = new List<EntityRef>();
            var filtered = f.Filter<CursedRiftInteraction>();

            while (filtered.Next(out EntityRef entity, out CursedRiftInteraction interaction))
            {
                if (interaction.State == CursedRiftInteractionState.SelectingSacrifice)
                    toCancel.Add(entity);
            }

            for (int i = 0; i < toCancel.Count; i++)
            {
                f.Remove<CursedRiftInteraction>(toCancel[i]);
            }

            if (toCancel.Count > 0)
                Log.Debug($"[RunPhase] Breathing ended - cancelled {toCancel.Count} uncommitted Cursed Rift interaction(s)");
        }

        // Unconditional sweep, unlike CancelUncommittedCursedRiftInteractions above - Store has no
        // "paid, reward still pending" multi-tick window at all (every purchase is one atomic
        // command, see StoreUtility.BuyWeapon/BuyFood), so there's nothing to distinguish by State.
        // A closed StoreInteraction costs the player nothing - StorePurchases already recorded
        // whatever they bought, and simply reopening the Store next Break re-rolls a fresh
        // inventory (see StoreUtility.EnsureInventoryRolled). See docs/store-blacksmith.md.
        public static void CloseStoreInteractionsOnBreathingEnd(Frame f)
        {
            List<EntityRef> toClose = new List<EntityRef>();
            var filtered = f.Filter<StoreInteraction>();

            while (filtered.Next(out EntityRef entity, out StoreInteraction _))
            {
                toClose.Add(entity);
            }

            for (int i = 0; i < toClose.Count; i++)
            {
                f.Remove<StoreInteraction>(toClose[i]);
            }

            if (toClose.Count > 0)
                Log.Debug($"[RunPhase] Breathing ended - closed {toClose.Count} open Store interaction(s)");
        }

        // Same unconditional shape as CloseStoreInteractionsOnBreathingEnd above - a Blacksmith
        // pick is also a single atomic command (BlacksmithUtility.SelectPerk), so there's no
        // "committed but unresolved" state to preserve past a Break ending. A closed
        // BlacksmithInteraction costs nothing either - PoiUsage was only ever marked on an actual
        // successful pick, never on the roll itself.
        public static void CloseBlacksmithInteractionsOnBreathingEnd(Frame f)
        {
            List<EntityRef> toClose = new List<EntityRef>();
            var filtered = f.Filter<BlacksmithInteraction>();

            while (filtered.Next(out EntityRef entity, out BlacksmithInteraction _))
            {
                toClose.Add(entity);
            }

            for (int i = 0; i < toClose.Count; i++)
            {
                f.Remove<BlacksmithInteraction>(toClose[i]);
            }

            if (toClose.Count > 0)
                Log.Debug($"[RunPhase] Breathing ended - closed {toClose.Count} open Blacksmith interaction(s)");
        }

        // One-shot side effect for the Survival/Breathing -> Boss edge (see
        // CombatDirectorSystem.ApplyPhaseGameState) - pulls every connected player into the Boss
        // Arena, seals it, spawns the boss(es), then briefly hard-pauses (GameplaySystemGroup
        // disabled) so the Boss Window reveal (BossWindow.cs, triggered separately from BossWidget
        // once it finds the freshly-spawned boss) plays with nothing able to act - confirmed with
        // the user. All steps up to the pause are gated on a Boss Arena chunk actually existing
        // (guaranteed exactly one per level, but this fails loud rather than teleporting/spawning
        // into a garbage position if that guarantee is ever violated).
        public static void BeginBossEncounter(Frame f, SurvivalPhase phase)
        {
            if (LevelGenerationSystem.TryFindBossArenaChunk(f, out EntityRef bossChunk) == false)
            {
                Log.Error("[RunPhase] Boss phase began but no Boss Arena chunk was found - encounter not triggered");
                return;
            }

            List<FPVector3> teleportPositions = new List<FPVector3>();
            LevelGenerationSystem.ResolveBossTeleportPositions(f, bossChunk, teleportPositions);
            GroundCorrect(f, teleportPositions);
            TeleportPlayersToBossArena(f, teleportPositions);

            EnableBossArenaGates(f);

            List<FPVector3> spawnPositions = new List<FPVector3>();
            LevelGenerationSystem.ResolveBossSpawnPositions(f, bossChunk, spawnPositions);
            GroundCorrect(f, spawnPositions);

            for (int i = 0; i < spawnPositions.Count; i++)
            {
                SpawnBoss(f, phase, spawnPositions[i]);
            }

            // Freezes everything inside GameplaySystemGroup (player movement/weapons/skills,
            // EnemySystem/BossSystem AI, KCC, the fall systems, ...) for phase.PauseDuration
            // seconds - the same f.SystemDisable<GameplaySystemGroup>() mechanism
            // LevelUpUtility.OpenUpgradeScreen already uses, just auto-timed instead of
            // player-choice-driven. BossPauseSystem (registered OUTSIDE the group, since it has to
            // keep ticking while the group is disabled) counts this down and re-enables the group
            // once it reaches 0 - see GameState.qtn's own BossPauseTimer comment.
            if (phase.PauseDuration > FP._0)
            {
                f.SystemDisable<GameplaySystemGroup>();
                f.Global->BossPauseTimer = phase.PauseDuration;
            }
        }

        // A hand-baked marker's Y (or the chunk's own raw authored pivot, for the no-markers
        // fallback) isn't necessarily the actual walkable floor - same top-down ground raycast
        // every other spawn in this codebase already uses (GroupSpawnerUtility.TrySpawnGroup)
        // instead of trusting either one blindly, so nothing teleported/spawned here lands inside
        // floor/prop geometry.
        private static void GroundCorrect(Frame f, List<FPVector3> positions)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            for (int i = 0; i < positions.Count; i++)
            {
                if (EnemyMovementUtility.TryFindGroundHeight(f, positions[i], groundLayerMask, out FP groundY) == true)
                {
                    positions[i] = new FPVector3(positions[i].X, groundY, positions[i].Z);
                }
            }
        }

        // KCC.Teleport plus zeroing every velocity source so momentum from wherever the player was
        // standing doesn't carry into the arena. Player i lands on
        // teleportPositions[i % teleportPositions.Count] - wraps around if fewer points than
        // players are authored (always at least 1, the geometric-center fallback), spreads players
        // across up to 4 hand-placed points otherwise instead of everyone stacking on one spot.
        private static void TeleportPlayersToBossArena(Frame f, List<FPVector3> teleportPositions)
        {
            List<EntityRef> players = LevelUpUtility.GetConnectedPlayers(f);

            for (int i = 0; i < players.Count; i++)
            {
                EntityRef player = players[i];
                FPVector3 destination = teleportPositions[i % teleportPositions.Count];

                if (f.Unsafe.TryGetPointer<KCC>(player, out var kcc) == true)
                {
                    kcc->Teleport(f, destination);
                    kcc->SetKinematicVelocity(FPVector3.Zero);
                    kcc->SetDynamicVelocity(FPVector3.Zero);
                    kcc->SetExternalImpulse(FPVector3.Zero);
                }
                else if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == true)
                {
                    transform->Position = destination;
                }
            }

            Log.Debug($"[RunPhase] Boss encounter began - teleported {players.Count} player(s) across {teleportPositions.Count} point(s)");
        }

        // Hand-placed, hand-authored colliders (see BossEncounter.qtn's own comment) - this only
        // flips them on, it never decides where they are or how many exist. BossArenaGateSystem
        // already forces each one's PhysicsCollider3D.Enabled to false the instant it's created, so
        // there's nothing to verify here about their starting state.
        private static void EnableBossArenaGates(Frame f)
        {
            var filtered = f.Filter<BossArenaGate>();
            int enabledCount = 0;

            while (filtered.Next(out EntityRef entity, out BossArenaGate _))
            {
                if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
                {
                    collider->Enabled = true;
                    enabledCount++;
                }
                else
                {
                    Log.Error($"[RunPhase] {entity} has a BossArenaGate tag but no PhysicsCollider3D - nothing to enable");
                }
            }

            Log.Debug($"[RunPhase] Boss encounter began - enabled {enabledCount} arena gate collider(s)");
        }

        // Mirrors GroupSpawnerUtility.SpawnMember's own f.Create -> position -> SeedFromEnemyData
        // sequence, minus EnemyLifecycle - deliberately not added, same choice already made and
        // confirmed safe for SpawnPackDeliveryData's own pack adds (only EnemyLifecycleSystem/
        // CombatDirectorUtility read that component and both already ignore entities without it).
        // A boss should never auto-retire via the Irrelevant timeout, and Director pressure/cap
        // accounting is moot anyway once TryPulse has stopped running entirely (see
        // CombatDirectorSystem's own gate). phase.BossPrototype is expected to already carry its
        // own EnemyData/BossRuntimeState/EnemySequenceState baked in, same as any other
        // self-contained one-off prototype in this codebase (Chests, POIs). Called once per
        // resolved spawn position - see BeginBossEncounter - so 2+ authored BossSpawnPoints spawn
        // that many copies of the same boss.
        private static void SpawnBoss(Frame f, SurvivalPhase phase, FPVector3 position)
        {
            if (phase.BossPrototype.Id.IsValid == false)
            {
                Log.Error("[RunPhase] Boss phase began but SurvivalPhase.BossPrototype isn't assigned - no boss spawned");
                return;
            }

            EntityRef entity = f.Create(phase.BossPrototype);

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
            {
                Log.Error("[RunPhase] SurvivalPhase.BossPrototype has no Enemy component - destroying spawned entity");
                f.Destroy(entity);
                return;
            }

            f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            EnemySystem.SeedFromEnemyData(f, entity, data);

            Log.Debug($"[RunPhase] Boss spawned: {entity} ({data?.name ?? "NULL EnemyDataAsset"}) at {position}");
        }

        // Fires exactly once per phase entry (Global.PhaseGuaranteedSpawnDone, reset by
        // SurvivalProgressionUtility.Tick on every CurrentPhaseIndex change) - closes the gap where
        // a phase's only spawn source is CombatDirectorUtility.TryPulse, which can silently skip a
        // purchase if the map is already crowded (see SurvivalConfig.cs's own GuaranteedGroup
        // comment). Deliberately reuses GroupSpawnerUtility.TrySpawnGroup - same formation/ground/
        // clearance search and EnemyLifecycle bookkeeping every normal Director purchase gets - and
        // only skips CombatDirectorUtility.TrySelectSpawn's budget/alive-cap gate, which is the one
        // gate a "guarantee" needs to bypass; a truly ungrounded/fully-blocked anchor still fails
        // loud (Log.Error) rather than spawning into geometry, same as every other spawn point in
        // this codebase. Anchored at PlayerClusterDirectorUtility's own GlobalCentroid - the same
        // point Elite+ ("major") groups already route to during a normal purchase - since a
        // guaranteed spawn has no per-front "neediest front" selection to run.
        public static void SpawnGuaranteedGroup(Frame f, SurvivalPhase phase, DirectorConfig directorConfig, BalanceConfig balanceConfig)
        {
            if (phase.GuaranteedGroup.Id.IsValid == false)
            {
                Log.Debug($"[RunPhase] {phase.Name} entered with no GuaranteedGroup assigned - nothing to guarantee-spawn");
                return;
            }

            EnemyGroupConfig group = f.FindAsset(phase.GuaranteedGroup);

            if (PlayerClusterDirectorUtility.BuildAnchors(f, phase, directorConfig, balanceConfig, out var plan) == false)
            {
                Log.Error($"[RunPhase] {phase.Name}'s GuaranteedGroup ({group.name}) has no players to anchor the spawn near - skipped");
                return;
            }

            // Anchored at GlobalCentroid regardless of tier (see this method's own comment above), so
            // it gets the same chunk-connectivity gate a normal purchase would only apply when the
            // group actually contains an Elite+ member - see CombatDirectorUtility.GroupContainsMajor.
            bool major = CombatDirectorUtility.GroupContainsMajor(f, group);

            if (GroupSpawnerUtility.TrySpawnGroup(f, group, phase.GuaranteedGroup, plan.GlobalCentroid, major, directorConfig, out int spawnedCount) == false)
            {
                Log.Error($"[RunPhase] {phase.Name}'s GuaranteedGroup ({group.name}) found no valid spawn anchor near {plan.GlobalCentroid} - nothing guaranteed-spawned this phase");
                return;
            }

            Log.Debug($"[RunPhase] {phase.Name} guaranteed-spawned {spawnedCount} member(s) of {group.name}");
        }

        // Called every tick from CombatDirectorSystem, BEFORE SurvivalProgressionUtility.Tick -
        // processes any SkipBreathingCommand sent this tick (idempotent re-vote), then, if every
        // currently-connected player has now voted for the CURRENT Breathing phase, force-ends it
        // THIS SAME TICK by setting PhaseTimer to the phase's own full Duration - Tick's own
        // existing ">= Duration" check then advances the phase exactly as if it had run out
        // naturally, no separate transition path to keep in sync. A no-op entirely outside a
        // Breathing phase (voting during Combat just pre-registers for whichever Breathing phase
        // comes next - harmless, see SkipBreathingCommand's own comment).
        public static void TryForceSkipBreathing(Frame f, SurvivalConfig config)
        {
            SurvivalPhase currentPhase = config.Phases[f.Global->CurrentPhaseIndex];

            ProcessSkipVotes(f);

            if (currentPhase.Kind != SurvivalPhaseKind.Breathing)
                return;

            if (AllConnectedPlayersVotedToSkip(f) == false)
                return;

            f.Global->PhaseTimer = currentPhase.Duration;
            Log.Debug("[RunPhase] every connected player voted to skip - ending Breathing early");
        }

        private static void ProcessSkipVotes(Frame f)
        {
            // A bot never sends a SkipBreathingCommand (nobody is holding its controller), so
            // without this the unanimity check below can never pass in a bot party and the human's
            // own Skip button would silently do nothing. Same "take the bot out of every
            // waiting-for-all-players gate" reasoning as LevelUpSystem.AutoPickForBots - the bot
            // votes yes as soon as the vote exists, so the human's vote alone decides. Opt-out via
            // RuntimeConfig.Bots.
            bool autoVoteBots = f.RuntimeConfig.Bots.DisableAutoBreathingSkipVote == false;
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                bool voted = f.GetPlayerCommand(playerLink.Player) is SkipBreathingCommand
                    || (autoVoteBots == true && f.Has<BotBrain>(entity) == true);

                if (voted == true)
                {
                    f.AddOrGet<BreathingSkipVote>(entity, out var vote);
                    vote->VotedAtBreathingIndex = f.Global->BreathingIndex;
                }
            }
        }

        // Presence (not just field value) is what "has this player voted" means - see
        // BreathingSkipVote's own comment for why a never-voted player has no component at all
        // rather than a default-0 field. Requires at least one connected player - an empty lobby
        // trivially "voting unanimously" would be a meaningless, confusing force-skip.
        private static bool AllConnectedPlayersVotedToSkip(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();
            bool anyPlayer = false;

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                anyPlayer = true;

                if (f.Unsafe.TryGetPointer<BreathingSkipVote>(entity, out var vote) == false
                    || vote->VotedAtBreathingIndex != f.Global->BreathingIndex)
                {
                    return false;
                }
            }

            return anyPlayer;
        }
    }
}
