namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Optional Team Challenge's own interaction - a WORLD-shared, one-shot team attempt (mirrors
    // TraversalChallengeUtility's own shape/reasoning): State alone (not PoiUsage) governs
    // re-triggerability, since the whole point is one shared attempt for the whole team, not a
    // per-player repeatable interaction. See TeamChallenge.qtn/docs.
    public static unsafe class TeamChallengeUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution and Busy already happened there via the sibling Interactable component).
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef poi)
        {
            if (f.Unsafe.TryGetPointer<TeamChallenge>(poi, out var challenge) == false)
                return ContextInteractionState.None;

            switch (challenge->State)
            {
                case TeamChallengeState.Available:
                case TeamChallengeState.WaitingForTeam:
                    if (PoiAvailabilityUtility.IsAvailable(f, challenge->Availability) == false)
                        return ContextInteractionState.PhaseUnavailable;

                    // Already Ready - leave the Ready/Cancel Area to cancel, a re-press does nothing
                    // (no arbitrary Ready timeout, no re-press-to-cancel either - see docs).
                    if (f.Unsafe.TryGetPointer<TeamChallengeReady>(player, out var ready) == true && ready->Poi == poi)
                        return ContextInteractionState.AlreadyUsed;

                    // Can't Ready up while an Elite is alive anywhere in the map - the challenge
                    // encounter is meant to read as its own clean, isolated fight, not one that
                    // starts on top of an ongoing Elite fight. NotNeeded (not a new state) since
                    // this is "eligible but pointless right now", the exact same shape
                    // HealingShrine's own full-HP case already uses - see
                    // InteractionPromptWidget.OnContextInteractionRejected for the toast this
                    // fires on an actual rejected press.
                    if (IsAnyEliteAlive(f) == true)
                        return ContextInteractionState.NotNeeded;

                    return ContextInteractionState.Available;

                case TeamChallengeState.RewardAvailable:
                    // Deliberately ignores PoiAvailability - "Survival continues while the reward
                    // waits" and ANY connected Raider may claim it whenever they return, Combat or
                    // Breathing alike.
                    return ContextInteractionState.Available;

                default:
                    // Starting/ChallengeActive/Completed/Failed - nothing a press can do right now.
                    return ContextInteractionState.AlreadyUsed;
            }
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-resolves state fresh (never trusts ContextInteraction.State alone) -
        // same reasoning TraversalChallengeUtility.TryActivate's own comment documents: two players
        // can both see a cached state from earlier this same tick and both press this same tick;
        // Quantum ticks single-threaded, so the first press's own state change is already visible to
        // the second's fresh re-read.
        public static void TryInteract(Frame f, EntityRef player, EntityRef poi)
        {
            ContextInteractionState state = ResolveInteractionState(f, player, poi);

            if (state == ContextInteractionState.NotNeeded)
            {
                // A real, deliberate press that does nothing (an Elite is alive right now) - fires
                // a View-only event so InteractionPromptWidget can show a ToastManager popup, same
                // "press did nothing, but the player should be told why" idiom
                // HealingShrineUtility.TryInteract already uses for its own NotNeeded case.
                f.Events.ContextInteractionRejected(player, poi);
                return;
            }

            if (state != ContextInteractionState.Available)
                return;

            var challenge = f.Unsafe.GetPointer<TeamChallenge>(poi);

            if (challenge->State == TeamChallengeState.RewardAvailable)
            {
                TryClaimReward(f, player, poi, challenge);
                return;
            }

            TryReadyUp(f, player, poi, challenge);
        }

        // Rolls SelectedChallenge on the very first Ready (Available->WaitingForTeam edge only),
        // adds this player's own Ready marker, fires the one-shot activation event. Unanimity is
        // NOT checked here - see TryBeginStarting, ticked every frame from TeamChallengeSystem so a
        // shrinking "required" count (a disconnect) can also complete unanimity with no new press.
        private static void TryReadyUp(Frame f, EntityRef player, EntityRef poi, TeamChallenge* challenge)
        {
            bool firstReady = challenge->State == TeamChallengeState.Available;

            if (firstReady == true)
            {
                // Normally already rolled by now - see EnsureChallengeRolled's own comment (rolled
                // the instant TeamChallengeSystem first ticks this entity while Available, long
                // before any player could physically reach it and press Interact). This call is
                // just the defensive fallback for the - practically unreachable - case that somehow
                // never happened; a misconfigured POI (no Config/ChallengePool authored) stays
                // Available and simply refuses every press, rather than advancing into
                // WaitingForTeam with no SelectedChallenge for TickWaitingForTeam/TryBeginStarting
                // to later read.
                if (EnsureChallengeRolled(f, poi, challenge) == false)
                    return;

                challenge->State = TeamChallengeState.WaitingForTeam;
            }

            f.Add<TeamChallengeReady>(player, out var ready);
            ready->Poi = poi;

            if (firstReady == true)
            {
                f.Events.TeamChallengeActivated(poi, player);
            }

            Log.Debug($"[TeamChallenge] {poi} - {player} Ready ({CountReady(f, poi)}/{CountRequiredRaiders(f)})");
        }

        // Rolls SelectedChallenge exactly once, the first opportunity it's ever checked - normally
        // called from TeamChallengeSystem.Update the very first tick it ticks this entity while
        // Available (see that system's own comment), so by the time any player could physically
        // reach the POI and press Interact it has long since been rolled - letting the Available-
        // state prompt (InteractionPromptWidget) describe the ACTUAL rolled ChallengeDefinition
        // (its own Description field) instead of a generic placeholder. Idempotent - a cheap no-op
        // returning true once already rolled, so calling it defensively from TryReadyUp too is free.
        public static bool EnsureChallengeRolled(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            if (challenge->SelectedChallenge.Id.IsValid == true)
                return true;

            return RollChallenge(f, poi, challenge);
        }

        private static bool RollChallenge(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            if (challenge->Config.Id.IsValid == false)
            {
                Log.Error($"[TeamChallenge] {poi} has no TeamChallengeConfig assigned - cannot roll a challenge");
                return false;
            }

            TeamChallengeConfig config = f.FindAsset(challenge->Config);

            if (config.ChallengePool == null || config.ChallengePool.Length == 0)
            {
                Log.Error($"[TeamChallenge] {poi}'s TeamChallengeConfig has no ChallengePool authored - cannot roll a challenge");
                return false;
            }

            int index = f.RNG->Next(0, config.ChallengePool.Length);
            challenge->SelectedChallenge = config.ChallengePool[index];

            Log.Debug($"[TeamChallenge] {poi} rolled {challenge->SelectedChallenge}");
            return true;
        }

        // Live recount, never cached - a connected player is one with a PlayerLink entity at all
        // (same LevelUpUtility.GetConnectedPlayers idiom), minus anyone currently Downed/KO: an
        // incapacitated Raider can't press Interact themselves right now (ContextInteractionSystem
        // already zeroes their own ContextInteraction), so they shouldn't be able to indefinitely
        // block the rest of the team from ever reaching unanimity. Public so TeamChallengeView can
        // render the same live "Y" in the X/Y READY HUD readout.
        public static int CountRequiredRaiders(Frame f)
        {
            int count = 0;
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, entity) == false)
                    count++;
            }

            return count;
        }

        // Live recount of Raiders currently Ready for THIS poi specifically - public for the same
        // TeamChallengeView HUD reason as CountRequiredRaiders above.
        public static int CountReady(Frame f, EntityRef poi)
        {
            int count = 0;
            var filtered = f.Filter<TeamChallengeReady>();

            while (filtered.Next(out EntityRef _, out TeamChallengeReady ready))
            {
                if (ready.Poi == poi)
                    count++;
            }

            return count;
        }

        // A bot has nobody at the keyboard to walk it into the Ready/Cancel Area and press Interact,
        // so (unless opted out) it Readies up unconditionally the instant WaitingForTeam begins -
        // same "take the bot out of a waiting-for-all-players gate" precedent
        // RunPhaseUtility.ProcessSkipVotes already establishes for the Breathing skip vote, so a
        // human party's own Ready presses alone are enough to reach unanimity. Called every tick
        // from TeamChallengeSystem while WaitingForTeam - a freshly-connected/freshly-spawned bot is
        // picked up the very next tick, not just once.
        public static void AutoReadyBots(Frame f, EntityRef poi)
        {
            if (f.RuntimeConfig.Bots.DisableAutoTeamChallengeReady == true)
                return;

            var filtered = f.Filter<PlayerLink, BotBrain>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _, out BotBrain _))
            {
                if (f.Has<TeamChallengeReady>(entity) == true)
                    continue;

                f.Add<TeamChallengeReady>(entity, out var ready);
                ready->Poi = poi;
            }
        }

        // View-only enumeration for InteractionPromptWidget's own per-player "challenge area" ready
        // icons - fills `readyStates` with one bool per currently-required Raider (same live
        // connected/non-incapacitated recount as CountRequiredRaiders) and returns how many were
        // written. Order is display-only (not gameplay-relevant, and only stable within a single
        // tick) - same live-recount idiom as CountReady/CountRequiredRaiders above.
        public static int GetReadyStates(Frame f, EntityRef poi, Span<bool> readyStates)
        {
            int count = 0;
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, entity) == true)
                    continue;

                if (count >= readyStates.Length)
                    break;

                readyStates[count] = f.Unsafe.TryGetPointer<TeamChallengeReady>(entity, out var ready) == true && ready->Poi == poi;
                count++;
            }

            return count;
        }

        // Called every tick from TeamChallengeSystem while WaitingForTeam (AFTER that tick's own
        // Ready/Cancel Area sweep) - checked continuously, not just on a fresh Ready press, so a
        // disconnect that shrinks CountRequiredRaiders down to the current Ready count also reaches
        // unanimity with no new press required (see docs' own disconnect edge case).
        public static void TryBeginStarting(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            if (challenge->State != TeamChallengeState.WaitingForTeam)
                return;

            int required = CountRequiredRaiders(f);
            int ready = CountReady(f, poi);

            if (required <= 0 || ready < required)
                return;

            // Same Elite-alive gate ResolveInteractionState uses to block a fresh Ready press -
            // re-checked here too (not just at press time) so an Elite spawning AFTER every
            // Raider already readied still holds the attempt in WaitingForTeam instead of
            // starting on top of it. No new Ready press needed once it clears, same "re-checked
            // every tick" shape this method's own disconnect-shrinks-required case already uses.
            if (IsAnyEliteAlive(f) == true)
                return;

            // Activation locked in: convert every live Ready marker into a permanent Participant tag
            // for this attempt - from this point on, leaving the Ready/Cancel Area no longer cancels
            // anything (TeamChallengeSystem's own cancel sweep only ever touches TeamChallengeReady,
            // which no longer exists on these entities).
            ConvertReadyToParticipants(f, poi);

            TeamChallengeConfig config = f.FindAsset(challenge->Config);
            challenge->RemainingCountdown = config.CountdownDuration;

            if (challenge->RemainingCountdown > FP._0)
            {
                challenge->State = TeamChallengeState.Starting;
                Log.Debug($"[TeamChallenge] {poi} - unanimous Ready ({ready}/{required}), counting down {challenge->RemainingCountdown}s");
            }
            else
            {
                BeginChallengeActive(f, poi, challenge);
            }
        }

        private static void ConvertReadyToParticipants(Frame f, EntityRef poi)
        {
            List<EntityRef> readyEntities = new List<EntityRef>();
            var filtered = f.Filter<TeamChallengeReady>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeReady ready))
            {
                if (ready.Poi == poi)
                    readyEntities.Add(entity);
            }

            for (int i = 0; i < readyEntities.Count; i++)
            {
                f.Remove<TeamChallengeReady>(readyEntities[i]);
                f.Add<TeamChallengeParticipant>(readyEntities[i], out var participant);
                participant->Poi = poi;
            }
        }

        // Called from TeamChallengeSystem once Starting's own countdown reaches 0 (or immediately
        // from TryBeginStarting above if CountdownDuration <= 0). The one call site that actually
        // pauses the Director (Global.ActiveTeamChallengeCount) and spawns the encounter.
        public static void BeginChallengeActive(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            challenge->State = TeamChallengeState.ChallengeActive;
            challenge->RemainingCountdown = FP._0;

            // The Elite-alive gate above only ever stops NEW readies/starts - nothing already
            // forces existing Survival enemies off the map, so without this they'd keep fighting
            // players right through what's meant to read as its own clean, isolated encounter.
            WipeAliveEnemies(f);

            ChallengeDefinition definition = f.FindAsset(challenge->SelectedChallenge);

            if (definition == null)
            {
                Log.Error($"[TeamChallenge] {poi} reached ChallengeActive with no valid SelectedChallenge - ending immediately as a failure");
                End(f, poi, challenge, success: false);
                return;
            }

            challenge->RemainingChallengeTime = definition.Duration;
            challenge->KillCount = 0;
            challenge->KillTarget = ResolveKillTarget(f, definition);
            challenge->PulseBudget = FP._0;
            challenge->PulseTimer = FP._0;

            f.Global->ActiveTeamChallengeCount++;

            if (definition.Type == ChallengeType.CursedSurvival)
            {
                CursedSurvivalUtility.ApplyCurse(f, poi);
            }

            f.Events.TeamChallengeStarted(poi);

            Log.Debug($"[TeamChallenge] {poi} - ChallengeActive begins ({definition.Type}, Duration={definition.Duration}, KillTarget={challenge->KillTarget})");
        }

        // Kill Rush/Flawless Hunt's own KillTarget scales with the SAME generic co-op pressure row
        // the normal Director already uses to scale enemy quantity/pressure per player count
        // (BalanceConfig.CoopGlobalKey.DirectorPressure, via PlayerClusterDirectorUtility.
        // ResolveCoopPressure) - reusing that existing row rather than inventing a Challenge-
        // specific multiplier, so a 4-player team is asked to kill proportionally more than a solo
        // Raider, exactly as if this were a normal Survival encounter scaled for that many players.
        // Public so InteractionPromptWidget's own rules-area preview (ChallengeRuleEntry.
        // ScaleTextWithKillTarget) can show this SAME live number before the challenge ever begins,
        // instead of a generic flat count that wouldn't match what BeginChallengeActive actually uses.
        public static int ResolveKillTarget(Frame f, ChallengeDefinition definition)
        {
            BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);
            FP coopPressure = PlayerClusterDirectorUtility.ResolveCoopPressure(balance, f.PlayerConnectedCount);
            return FPMath.RoundToInt(definition.KillTarget * coopPressure);
        }

        // Paces the challenge's own encounter through the EXACT SAME budget/pressure/purchase
        // algorithm normal Survival phases use (CombatDirectorUtility.TryPulse) - called every tick
        // from TeamChallengeSystem while ChallengeActive, fed from ChallengeDefinition's own
        // phase-shaped fields instead of SurvivalConfig.Phases[], against this challenge's OWN
        // PulseBudget/PulseTimer pool (never Global.DirectorBudget/DirectorPulseTimer - the normal
        // Director is paused throughout anyway via Global.ActiveTeamChallengeCount, but a separate
        // pool means neither run ever inherits the other's leftover budget). TryPulse's own
        // PlayerClusterDirectorUtility.BuildAnchors call already anchors on live player cluster
        // positions - the same co-op split-front behavior normal Survival gets, just naturally
        // centered near the POI since that's where every participant is standing.
        public static void PulseChallengeEncounter(Frame f, EntityRef poi, TeamChallenge* challenge, ChallengeDefinition definition)
        {
            if (f.RuntimeConfig.DirectorConfig.Id.IsValid == false || f.RuntimeConfig.LifecycleConfig.Id.IsValid == false)
            {
                Log.Error("[TeamChallenge] RuntimeConfig.DirectorConfig/LifecycleConfig not fully assigned - challenge encounter stays idle");
                return;
            }

            DirectorConfig directorConfig = f.FindAsset(f.RuntimeConfig.DirectorConfig);
            LifecycleConfig lifecycleConfig = f.FindAsset(f.RuntimeConfig.LifecycleConfig);
            BalanceConfig balanceConfig = f.FindAsset(f.RuntimeConfig.BalanceConfig);

            SurvivalPhase syntheticPhase = new SurvivalPhase
            {
                BudgetPerPulse = definition.BudgetPerPulse,
                PulseInterval = definition.PulseInterval,
                TargetPressure = definition.TargetPressure,
                MaxAliveEnemies = definition.MaxAliveEnemies,
                AllowedGroups = definition.AllowedGroups,
                AllowedEnemies = definition.AllowedEnemies,
            };

            // Snapshot BEFORE the pulse, not an EntityRef/index watermark - EntityRef indices can be
            // recycled after a destroy, so "every match not in this exact before-set" is the only
            // fully correct way to isolate exactly what THIS pulse just created. A single pulse can
            // make several purchases (any mix of AllowedGroups/AllowedEnemies), so this snapshots
            // every EnemyLifecycle-carrying entity, not just one group's own.
            List<EntityRef> before = CollectAllDirectorEnemies(f);

            CombatDirectorUtility.TryPulse(f, syntheticPhase, directorConfig, lifecycleConfig, balanceConfig,
                &challenge->PulseTimer, &challenge->PulseBudget);

            TagNewlySpawnedEnemies(f, poi, before);
        }

        private static List<EntityRef> CollectAllDirectorEnemies(Frame f)
        {
            List<EntityRef> entities = new List<EntityRef>();
            var filtered = f.Filter<EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out EnemyLifecycle _))
            {
                entities.Add(entity);
            }

            return entities;
        }

        private static void TagNewlySpawnedEnemies(Frame f, EntityRef poi, List<EntityRef> before)
        {
            var filtered = f.Filter<EnemyLifecycle>();

            while (filtered.Next(out EntityRef entity, out EnemyLifecycle _))
            {
                if (before.Contains(entity) == true)
                    continue;

                f.Add<TeamChallengeSpawn>(entity, out var spawn);
                spawn->Poi = poi;
            }
        }

        // Live check for whether ANY Team Challenge POI is currently Starting or ChallengeActive -
        // used only by CombatDirectorSystem.ApplyEffectiveState, since Global.ActiveTeamChallengeCount
        // itself only increments once ChallengeActive truly begins (Survival keeps running normally
        // through the Starting countdown - see docs' own "STARTING THE CHALLENGE"), but the
        // countdown itself still needs this same GameState.TeamChallenge overlay to show in.
        public static bool AnyBannerActive(Frame f)
        {
            var filtered = f.Filter<TeamChallenge>();

            while (filtered.Next(out EntityRef _, out TeamChallenge challenge))
            {
                if (challenge.State == TeamChallengeState.Starting || challenge.State == TeamChallengeState.ChallengeActive)
                    return true;
            }

            return false;
        }

        // The one terminal-transition funnel - called from ChallengeObjectiveUtility.Tick (timeout/
        // Cursed Survival poll), TeamChallengeReactionSystem (Kill Rush/Flawless Hunt signals), and
        // TeamChallengeSystem's own RunFailed/Victory interruption safety check. Guarded on
        // ChallengeActive so a same-tick race between two of those callers can only ever end the
        // challenge once.
        public static void End(Frame f, EntityRef poi, TeamChallenge* challenge, bool success)
        {
            if (challenge->State != TeamChallengeState.ChallengeActive)
                return;

            DestroyChallengeSpawns(f, poi);
            CursedSurvivalUtility.RestoreCurse(f, poi);
            RemoveParticipants(f, poi);
            Decrement(f);

            challenge->RemainingChallengeTime = FP._0;

            // Only the FAILURE path settles here - success instead goes to RewardAvailable, and
            // only reaches the real terminal TeamChallengeState.Completed once the shared reward is
            // actually claimed (see TryClaimReward). Failure has no reward to wait on, so it goes
            // straight to its own terminal value.
            challenge->State = success ? TeamChallengeState.RewardAvailable : TeamChallengeState.Failed;

            if (success == true)
                f.Events.TeamChallengeCompleted(poi);
            else
                f.Events.TeamChallengeFailed(poi);

            Log.Debug($"[TeamChallenge] {poi} - ended, success={success}, new State={challenge->State}");
        }

        // Mirrors TraversalChallengeUtility.Fail's own platform-destroy sweep - guarantees Survival
        // resumes with no leftover challenge enemies polluting normal pressure/alive-cap accounting.
        private static void DestroyChallengeSpawns(Frame f, EntityRef poi)
        {
            List<EntityRef> toDestroy = new List<EntityRef>();
            var filtered = f.Filter<TeamChallengeSpawn>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeSpawn spawn))
            {
                if (spawn.Poi == poi)
                    toDestroy.Add(entity);
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (f.Exists(toDestroy[i]) == false)
                    continue;

                // Still alive - same death VFX/no-reward treatment WipeAliveEnemies uses below, so
                // a leftover challenge enemy never just vanishes when the attempt ends. An
                // already-Dead one (mid its own real DeathLingerTime from an actual kill, already
                // excluded from every alive/pressure count) is just force-destroyed outright - it
                // already played its own death effect for real.
                if (f.Unsafe.TryGetPointer<Enemy>(toDestroy[i], out var enemy) == true && enemy->Phase != EnemyActionPhase.Dead)
                    KillWithoutRewards(f, toDestroy[i]);
                else
                    f.Destroy(toDestroy[i]);
            }

            if (toDestroy.Count > 0)
                Log.Debug($"[TeamChallenge] {poi} - cleared {toDestroy.Count} leftover challenge-spawned enemy(ies)");
        }

        // Live filter every time, never a maintained flag - same "read live" idiom
        // CountReady/CountRequiredRaiders/AnyBannerActive above already use, so this can never
        // desync from what's actually alive. Phase == Dead is excluded, same convention
        // SurvivalProgressionUtility.IsEncounterCleared's own Elite/Boss check already follows -
        // a lingering corpse isn't "alive" for this purpose either.
        private static bool IsAnyEliteAlive(Frame f)
        {
            var filtered = f.Filter<Enemy>();

            while (filtered.Next(out EntityRef _, out Enemy enemy))
            {
                if (enemy.Phase == EnemyActionPhase.Dead)
                    continue;

                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);

                if (data != null && data.Tier == EnemyTier.Elite)
                    return true;
            }

            return false;
        }

        // Wipes every enemy still standing in the world the instant ChallengeActive begins (see
        // BeginChallengeActive) - normal Survival enemies would otherwise keep fighting players
        // right through what's meant to read as a clean, isolated challenge encounter. Also reused
        // by DestroyChallengeSpawns above for the challenge's OWN leftover enemies at End.
        private static void WipeAliveEnemies(Frame f)
        {
            List<EntityRef> toClear = new List<EntityRef>();
            var filtered = f.Filter<Enemy>();

            while (filtered.Next(out EntityRef entity, out Enemy enemy))
            {
                if (enemy.Phase != EnemyActionPhase.Dead)
                    toClear.Add(entity);
            }

            for (int i = 0; i < toClear.Count; i++)
            {
                KillWithoutRewards(f, toClear[i]);
            }
        }

        // Plays the exact same death VISUAL a normal kill's own ResolveDeath (DamageUtility) does -
        // DamageUtility.FireEnemyExploded for the immediate-explode tiers, the Dead-phase corpse
        // linger + OnEnemyDied signal for the rest - but skips the entire on-kill PIPELINE
        // (EntityDied/OnEntityKilled event/signal, XP/Scrap/RiftShard/Coin/Chest drops,
        // MonstersKilled credit). This is a map reset the Director/challenge triggers, not a kill
        // any player earned, so nothing should reward or proc off it.
        private static void KillWithoutRewards(Frame f, EntityRef target)
        {
            if (f.Exists(target) == false || f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            if (enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            if (data == null)
            {
                f.Destroy(target);
                return;
            }

            // Same tier split DamageUtility.ResolveDeath uses for a normal kill's own death handling.
            if (data.Tier == EnemyTier.Filler || data.Tier == EnemyTier.Normal || data.Tier == EnemyTier.Heavy || data.Tier == EnemyTier.Specialist)
            {
                DamageUtility.FireEnemyExploded(f, target, enemy->EnemyData);
                f.Destroy(target);
            }
            else
            {
                enemy->Phase = EnemyActionPhase.Dead;
                enemy->StateTimer = data.DeathLingerTime;
                f.Signals.OnEnemyDied(target);
            }
        }

        private static void RemoveParticipants(Frame f, EntityRef poi)
        {
            List<EntityRef> toRemove = new List<EntityRef>();
            var filtered = f.Filter<TeamChallengeParticipant>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeParticipant participant))
            {
                if (participant.Poi == poi)
                    toRemove.Add(entity);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                f.Remove<TeamChallengeParticipant>(toRemove[i]);
            }
        }

        // Clamped at 0 as cheap insurance against a future double-decrement bug permanently freezing
        // SurvivalTime/spawning for the rest of the run - same idiom TraversalChallengeUtility.
        // Decrement already uses for its own counter.
        private static void Decrement(Frame f)
        {
            if (f.Global->ActiveTeamChallengeCount > 0)
                f.Global->ActiveTeamChallengeCount--;
        }

        // Atomic claim - the state guard alone is sufficient with no interstitial state, since
        // Quantum ticks single-threaded/deterministically and ResolveInteractionState/TryInteract
        // always re-resolve state fresh (never trust a same-tick cached ContextInteraction.State) -
        // see TryInteract's own comment. Reuses the exact same "one interaction rolls an independent
        // 3-Rift-Mutation choice for every connected player, pausing the whole party" mechanism a
        // Chest already uses - zero new roll/UI code.
        private static void TryClaimReward(Frame f, EntityRef player, EntityRef poi, TeamChallenge* challenge)
        {
            if (challenge->State != TeamChallengeState.RewardAvailable)
                return;

            challenge->State = TeamChallengeState.Completed;

            Log.Debug($"[TeamChallenge] {poi} - reward claimed by {player}, opening independent Rift Mutation picks for every connected Raider");

            LevelUpUtility.BeginChestScreen(f, player, LevelUpCategory.RiftMutation);
        }
    }
}
