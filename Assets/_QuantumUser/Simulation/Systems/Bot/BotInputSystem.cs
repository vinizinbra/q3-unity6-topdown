namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // The brain behind every RuntimePlayer.IsBot slot (see BotBrain.qtn / docs/bots.md) - a
    // local-testing convenience so one person can play a full co-op party and watch a hero's kit
    // fire in a real match instead of an empty room.
    //
    // Deliberately the smallest thing that works: follow the first human player, hold Run when
    // falling behind, and pulse Dash / Hero Skill on their own randomized countdowns. There is no
    // pathfinding, no combat positioning and no kiting - a bot exists to be a moving, shooting,
    // level-ing body next to the player, not to play well. Everything that makes it feel like a
    // real character is already free: it writes the same Input struct a human does, so
    // PlayerMovementProcessor drives it (including auto-hop/auto-mantle via AutoJumpSystem),
    // AimSystem picks its targets and WeaponSystem auto-attacks off Aim.Target with no Fire input
    // at all - which is why this never touches Input.Fire.
    //
    // Runs inside GameplaySystemGroup immediately before KCCSystem, so the decision made here is
    // the one this same tick's movement resolves; being inside the group also means a bot freezes
    // with everyone else while an upgrade screen is open. See SystemSetup.User.cs.
    [Preserve]
    public unsafe class BotInputSystem : SystemMainThreadFilter<BotInputSystem.Filter>
    {
        // Fallbacks for an unauthored (all-zero) RuntimeConfig.Bots - see BotSettings' own comment
        // for why a struct can't carry field initializers and every FP there means "0 = default".
        private static readonly FP DefaultFollowDistance = 3;
        private static readonly FP DefaultFollowSlack = FP._1;
        private static readonly FP DefaultRunDistance = 6;
        private static readonly FP DefaultLeashDistance = 25;
        private static readonly FP DefaultLeashTimeout = 3;
        private static readonly FP DefaultHeroSkillIntervalMin = 6;
        private static readonly FP DefaultHeroSkillIntervalMax = 12;
        private static readonly FP DefaultDashIntervalMin = 4;
        private static readonly FP DefaultDashIntervalMax = 9;
        private static readonly FP DefaultHeroSkillEnemyRange = 12;
        private static readonly FP DefaultFormationOffsetMin = 2;
        private static readonly FP DefaultFormationOffsetMax = 4;
        private static readonly FP DefaultFormationRerollIntervalMin = 5;
        private static readonly FP DefaultFormationRerollIntervalMax = 10;
        private static readonly FP DefaultSoloRepickInterval = 4;
        private static readonly FP DefaultSoloSearchRange = 20;

        // Arrival distances for a solo bot's own goal (see UpdateSoloSurvival) - a Chunk needs the
        // bot genuinely standing inside its AABB for ChunkDiscoverySystem to flip Discovered, while
        // an XP orb only needs the bot close enough for CurrencyOrbSystem's own pickup radius to
        // reach it. Deliberately smaller than DefaultFollowDistance, which is sized for parking near
        // a PLAYER, not walking onto a pickup.
        private static readonly FP SoloChunkArrivalDistance = FP._1;
        private static readonly FP SoloOrbArrivalDistance = FP._0_50;

        // Brute-force local pathfinder (see TryFindGridPath) - grid cell size, and how far past the
        // straight line between self and target the search area extends on every side.
        private static readonly FP GridStep = 2;
        private static readonly FP GridSearchPadding = 4;

        // How big a height jump between two ADJACENT grid cells still counts as one walkable floor
        // rather than two disconnected ones (e.g. a real cliff edge sampled from both sides) - same
        // spirit as LedgeMaxDropDistance, just applied cell-to-cell instead of step-to-step.
        private static readonly FP GridStepMaxHeightDelta = 4;
        private static readonly FP GridWaypointArrivalDistance = FP._1;

        // Hard cap on how many cells one grid search will ever raycast, regardless of how far apart
        // self/target are - a farther target coarsens the grid (bigger GridStep) rather than
        // searching more cells, so one blocked-route event never spikes tick cost unboundedly.
        private const int MaxGridCellsVisited = 300;

        // Must match BotBrain.DetourPath's own qtn array size - a path longer than this is treated as
        // not found (see TryFindGridPath) rather than silently truncated. TryFindGridPath now mostly
        // reroutes a single blocked LEG of a macro chunk route (see MaxRouteChunkWaypoints/
        // TryFindChunkRoutePath), which is normally short, but it's also the fallback for the
        // same-chunk case and for whenever chunk routing can't resolve a containing chunk at all -
        // TryFindGridPath scales its own step size up for a farther target to stay inside this
        // budget regardless of which case triggered it.
        private const int MaxGridPathWaypoints = 32;

        // Must match BotBrain.RoutePath's own qtn array size - a chunk sequence longer than this is
        // treated as not found (see TryFindChunkRoutePath). Chunk-level hops, not fine grid cells, so
        // this only needs to cover how many chunks a realistic level asks a bot to cross in one go.
        private const int MaxRouteChunkWaypoints = 16;

        // Follow distance used instead of the authored one while the target is Downed/KO. The bot
        // has to be inside the Revive Interactable's own radius (ReviveConfig) before
        // ContextInteractionSystem will ever report Available, and a normal ~3-unit follow stand-off
        // is comfortably outside it - so a bot would otherwise loiter next to a dying teammate it
        // is technically willing to revive. See UpdateSkills' Revive branch.
        private static readonly FP DownedTargetFollowDistance = FP._1;

        // Wall probe: how far ahead the steering sphere-cast looks, and how wide it is. Matches the
        // scale EnemyMovementUtility.MoveInDirection probes at for a normal-sized enemy - a bot is
        // a player-sized capsule, so the same numbers read the same way.
        private static readonly FP WallProbeDistance = FP._2;
        private static readonly FP WallProbeRadius = FP._0_50;

        // Ledge probe (see IsDirectionSafe). Deliberately reaches FURTHER than the player's own
        // auto-hop edge probe (MovementDataAsset.EdgeProbeDistance, 0.75) - auto-hop's reaction to
        // "no ground ahead" is to JUMP, so a bot that only noticed the void at auto-hop's distance
        // would already have been launched into it. Probing first, from further out, is what lets
        // the bot turn away before that fires.
        private static readonly FP LedgeProbeDistance = FP._1 + FP._0_50;

        // How far down still counts as ground rather than void. A drop this deep is survivable and
        // walkable-down; anything past it is treated as a pit. Generous enough to follow a player
        // down a real ledge, short enough that a bottomless gap never reads as floor.
        private static readonly FP LedgeMaxDropDistance = 4;

        // A gap with real ground on the far side within this distance is crossable - the player
        // auto-hop carries roughly 3 units at JumpVelocity 12 / Gravity -45 / RunSpeed 6, so this
        // stays well inside what the jump can actually clear. Mostly this exists for chunk SEAMS,
        // which are sub-unit and would otherwise read as a void and stop the bot dead.
        private static readonly FP LedgeMaxCrossableGap = FP._1 + FP._0_50;
        private static readonly FP LedgeGapScanStep = FP._0_25;

        // Deflection candidates tried, in order, when the direct route to the target runs off an
        // edge - mirrored pairs so the bot has no innate turn bias. 90 degrees is the useful
        // extreme: it walks ALONG the lip of a chasm rather than into it. Static is safe here
        // despite living in a system - this is immutable constant data, never written at runtime,
        // so it is not simulation state and nothing about it can desync or need rolling back.
        private static readonly FP[] LedgeDeflectionAngles = { 45, -45, 90, -90 };

        public override void Update(Frame f, ref Filter filter)
        {
            RuntimeConfig.BotSettings settings = f.RuntimeConfig.Bots;

            // Captured before the clear below - the follow hysteresis needs to know whether the
            // bot was moving LAST tick (see UpdateFollow).
            bool wasMoving = filter.Brain->Data.Direction != default;

            // Cleared every tick, so every button written below is a genuine one-tick pulse and a
            // WasPressed consumer can never see the same decision twice.
            filter.Brain->Data = default;

            TickTimers(f, filter.Brain, settings);

            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
                return;

            if (TryResolveFollowTarget(f, filter.Entity, out EntityRef target, out FPVector3 targetPosition) == false)
            {
                // No human or other bot left to trail - give this bot its own goal instead of
                // standing frozen for the rest of the run. See UpdateSolo.
                UpdateSolo(f, ref filter, settings);
                UpdateSkills(f, ref filter, settings, filter.Transform->Position);
                return;
            }

            // Follow mode never builds a macro RoutePath (only solo's EnsureRoutePath does) - clear
            // any left over from a previous solo stretch so TryMoveToward can't mistake it for one.
            ClearRoutePath(ref filter);

            bool targetIncapacitated = PlayerLifeStateUtility.IsIncapacitated(f, target);

            // A downed teammate needs the bot to walk to their EXACT position (inside the Revive
            // Interactable's own radius) - a formation slot a couple of units off would otherwise
            // strand the bot just out of range of the one interaction it actually needs to take.
            FPVector3 followPosition = targetIncapacitated == true
                ? targetPosition
                : targetPosition + ResolveFormationOffset(f, target, filter.Brain);

            FPVector3 selfPosition = filter.Transform->Position;
            FPVector3 delta = followPosition - selfPosition;
            delta.Y = FP._0;
            FP distance = delta.Magnitude;

            // Follow first, leash second: UpdateFollow is what discovers that every route to the
            // target runs off a ledge, and a bot pinned at the lip of a chasm needs the leash even
            // when the target is close enough that distance alone would never trigger it.
            bool blocked = UpdateFollow(f, ref filter, settings, delta, distance, wasMoving, targetIncapacitated);

            UpdateLeash(f, ref filter, settings, followPosition, distance, blocked);
            UpdateSkills(f, ref filter, settings, selfPosition);
        }

        private static void TickTimers(Frame f, BotBrain* brain, RuntimeConfig.BotSettings settings)
        {
            brain->HeroSkillTimer -= f.DeltaTime;
            brain->DashSkillTimer -= f.DeltaTime;

            brain->FormationRerollTimer -= f.DeltaTime;

            if (brain->FormationRerollTimer > FP._0)
                return;

            FP min = Or(settings.FormationOffsetMin, DefaultFormationOffsetMin);
            FP max = Or(settings.FormationOffsetMax, DefaultFormationOffsetMax);

            brain->FormationAngle = f.RNG->Next(FP._0, 360);
            brain->FormationDistance = max > min ? f.RNG->Next(min, max) : min;
            brain->FormationRerollTimer = RollInterval(f, settings.FormationRerollIntervalMin, settings.FormationRerollIntervalMax,
                DefaultFormationRerollIntervalMin, DefaultFormationRerollIntervalMax);
        }

        // Turns this bot's own (angle, distance) formation slot into a world-space offset, resolved
        // fresh every tick off the TARGET's current facing (Aim.Angle - the same source
        // AimSystem/DamageUtility already treat as a player's authoritative facing) so the slot
        // swings around as the target turns, even though the slot's own angle/distance only change
        // when FormationRerollTimer fires. 0 degrees is directly in front of the target, 180 behind.
        //
        // Known simplification: a target that spins in place (aiming around without moving) drags
        // every bot's slot around with it, so a bot can end up walking a small arc for no positional
        // reason. Accepted rather than adding a dead zone - see docs/bots.md's own "smallest thing
        // that works" framing.
        private static FPVector3 ResolveFormationOffset(Frame f, EntityRef target, BotBrain* brain)
        {
            FP leaderAngle = FP._0;

            if (f.Unsafe.TryGetPointer<Aim>(target, out Aim* aim) == true)
            {
                leaderAngle = aim->Angle;
            }

            FP worldAngle = leaderAngle + brain->FormationAngle;

            return FPQuaternion.Euler(FP._0, worldAngle, FP._0) * FPVector3.Forward * brain->FormationDistance;
        }

        // The bot follows the FIRST non-bot player - lowest PlayerRef wins, so a bot always trails
        // the same person for the whole run rather than swapping every time someone gets closer.
        // Falls back to the first OTHER bot only if there is no human at all (a bots-only session
        // is still worth watching), and gives up entirely if this bot is the only player left.
        private static bool TryResolveFollowTarget(Frame f, EntityRef self, out EntityRef target, out FPVector3 position)
        {
            EntityRef best = EntityRef.None;
            int bestPlayer = 0;
            bool bestIsHuman = false;
            position = default;

            var filtered = f.Filter<PlayerLink, Transform3D>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink, out Transform3D transform) == true)
            {
                if (entity == self)
                    continue;

                bool isHuman = f.Has<BotBrain>(entity) == false;
                int candidatePlayer = (int)playerLink.Player;

                // A human always beats a bot; between two of the same kind the lower PlayerRef wins.
                if (best != EntityRef.None
                    && ((bestIsHuman == true && isHuman == false)
                        || (bestIsHuman == isHuman && bestPlayer <= candidatePlayer)))
                {
                    continue;
                }

                best = entity;
                bestPlayer = candidatePlayer;
                bestIsHuman = isHuman;
                position = transform.Position;
            }

            target = best;
            return best != EntityRef.None;
        }

        // Recovery, not navigation: the follow steering below has no pathfinding, so a bot that
        // walks into a dead end or gets shoved behind geometry would otherwise be gone for the rest
        // of the run. Only fires after the bot has been genuinely far away for a sustained stretch,
        // never on a brief separation.
        private static void UpdateLeash(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FPVector3 targetPosition, FP distance, bool blocked)
        {
            FP leashDistance = Or(settings.LeashDistance, DefaultLeashDistance);
            FP leashTimeout = Or(settings.LeashTimeout, DefaultLeashTimeout);

            // Two ways to qualify. Far away for a sustained stretch is the original one. "Blocked"
            // is the ledge case: the bot WANTS to move but every route it probed runs into a pit
            // (see UpdateFollow), which can happen with the target only a few units away on the
            // far side of a chasm - a distance-only leash would leave it standing there forever.
            bool stranded = distance > leashDistance || blocked == true;

            if (leashDistance <= FP._0 || stranded == false)
            {
                filter.Brain->LeashTimer = FP._0;
                return;
            }

            filter.Brain->LeashTimer += f.DeltaTime;

            if (filter.Brain->LeashTimer < leashTimeout)
                return;

            filter.Brain->LeashTimer = FP._0;

            // Same KCC.Teleport idiom PlayerFallSystem/RunPhaseUtility already use for moving a
            // player - dropped in slightly above the target so the bot settles onto the ground
            // rather than into it. Velocity is zeroed for the same reason PlayerFallSystem does it:
            // KCC.Teleport moves the character but does NOT clear its velocity, so a bot teleported
            // mid-fall would arrive still falling.
            filter.KCC->Teleport(f, targetPosition + FPVector3.Up);
            filter.KCC->SetKinematicVelocity(FPVector3.Zero);
            filter.KCC->SetDynamicVelocity(FPVector3.Zero);
            filter.KCC->SetExternalImpulse(FPVector3.Zero);

            // Whatever direction was chosen this tick pointed at where the bot used to be standing.
            filter.Brain->Data.Direction = default;

            Log.Debug($"[Bot] {filter.Entity} was stranded for {leashTimeout}s ({(blocked == true ? "no safe route" : "past the leash distance")}) - teleported back to its follow target");
        }

        // Returns true when the bot WANTED to move but every route it probed runs off a ledge -
        // even after TryMoveToward's own brute-force detour attempt - that is what promotes it to
        // "stranded" for the leash (see UpdateLeash). Standing still because it is already close
        // enough is not blocked.
        private static bool UpdateFollow(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FPVector3 delta, FP distance, bool wasMoving, bool targetIncapacitated)
        {
            FP followDistance = targetIncapacitated == true
                ? DownedTargetFollowDistance
                : Or(settings.FollowDistance, DefaultFollowDistance);
            FP slack = Or(settings.FollowSlack, DefaultFollowSlack);

            // Hysteresis: a bot already walking closes all the way to followDistance, but one
            // that has parked won't set off again until the target is a full slack further out.
            // Without it a bot sitting exactly at followDistance stutters in and out of motion
            // every single tick.
            FP resumeDistance = wasMoving == true ? followDistance : followDistance + slack;

            if (distance <= resumeDistance || distance <= FP._0)
            {
                ClearDetourPath(ref filter);
                return false;
            }

            bool run = distance > Or(settings.RunDistance, DefaultRunDistance);
            FPVector3 selfPosition = filter.Transform->Position;
            FPVector3 targetPosition = selfPosition + delta;

            return TryMoveToward(f, ref filter, settings, selfPosition, targetPosition, run) == false;
        }

        // Two path tiers, checked in order:
        //   1. DetourPath - a short-range local grid detour (see TryFindGridPath) around whatever
        //      LEG is currently blocked. Always leads back to that leg's own destination (a
        //      RoutePath waypoint, or targetPosition directly) - never the far end of the whole
        //      journey - so a pond blocking one hop of a macro chunk route only reroutes THAT hop,
        //      the rest of RoutePath is untouched.
        //   2. RoutePath - the macro chunk-level route (see TryFindChunkRoutePath), if solo movement
        //      built one via EnsureRoutePath. Empty for a follow target (a live player moves every
        //      tick, so a macro route to it would be stale before it mattered) - in that case the
        //      "leg" is just targetPosition directly, same as it always was.
        // A blocked leg tries TryFindGridPath as tier 1 before giving up on it. Only once BOTH the
        // current leg's direct route AND a grid search for it have failed does this report "stuck"
        // (false) - the caller's own signal (UpdateFollow promotes it to the leash, solo wander
        // drops the goal and picks a new one).
        private static bool TryMoveToward(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FPVector3 selfPosition, FPVector3 targetPosition, bool run)
        {
            if (filter.Brain->DetourPathCursor < filter.Brain->DetourPathCount)
            {
                FPVector3 waypoint = filter.Brain->DetourPath[filter.Brain->DetourPathCursor];
                FPVector3 waypointDelta = waypoint - selfPosition;
                waypointDelta.Y = FP._0;
                FP waypointDistance = waypointDelta.Magnitude;

                if (waypointDistance <= GridWaypointArrivalDistance)
                {
                    filter.Brain->DetourPathCursor++;
                    return true;
                }

                bool waypointRun = waypointDistance > Or(settings.RunDistance, DefaultRunDistance);

                if (SteerToward(f, ref filter, waypointDelta, waypointRun) == true)
                {
                    // Even the local detour is blocked now (something moved) - drop it and retry
                    // fresh against the same leg target next tick rather than reporting the whole
                    // route stuck over one transient hiccup.
                    ClearDetourPath(ref filter);
                }

                return true;
            }

            FPVector3 legTarget = targetPosition;
            bool legIsFinal = true;

            if (filter.Brain->RoutePathCursor < filter.Brain->RoutePathCount)
            {
                legTarget = filter.Brain->RoutePath[filter.Brain->RoutePathCursor];
                legIsFinal = false;
            }

            FPVector3 delta = legTarget - selfPosition;
            delta.Y = FP._0;
            FP legDistance = delta.Magnitude;

            if (legIsFinal == false && legDistance <= GridWaypointArrivalDistance)
            {
                filter.Brain->RoutePathCursor++;
                return true;
            }

            bool legRun = legIsFinal == true ? run : legDistance > Or(settings.RunDistance, DefaultRunDistance);

            if (SteerToward(f, ref filter, delta, legRun) == false)
                return true;

            // This leg is blocked - try a local grid detour specifically TO this leg's own
            // destination (a RoutePath waypoint, or targetPosition directly if there's no macro
            // route), never the far end of the whole journey.
            return TryFindGridPath(f, ref filter, selfPosition, legTarget);
        }

        // Shared wall-deflection + ledge-avoidance steering toward a target-relative delta - the
        // follow, Store-walk and solo-wander paths all share the exact same "dumb" navigation
        // budget (see docs/bots.md "Wall deflection, no pathfinding."). Writes Brain.Data directly;
        // returns true when every direction tried runs off a ledge (the caller's own "blocked"
        // signal - see TryMoveToward, which every direct-movement caller now goes through).
        private static bool SteerToward(Frame f, ref Filter filter, FPVector3 delta, bool run)
        {
            FPVector2 direction = new FPVector2(delta.X, delta.Z).Normalized;

            // Same wall deflection an AvoidWalls enemy gets - turns "grinding into the corner
            // between here and the target" into "sliding along it", which is most of what keeps a
            // pathfinding-free walk usable inside chunk geometry.
            //
            // Probe from knee height, not the feet: a sphere cast starting flush with the floor
            // reports the floor itself as the obstacle (harmless - its normal has no horizontal
            // component, so SteerAroundWalls passes the direction straight through - but it also
            // means the real wall behind it is never found).
            FPVector3 probeOrigin = filter.Transform->Position + FPVector3.Up * WallProbeRadius;

            direction = EnemyMovementUtility.SteerAroundWalls(f, probeOrigin, direction,
                WallProbeDistance, WallProbeRadius, EnemyMovementUtility.GetGroundLayerMask(f));

            // Wall deflection resolved WHERE to walk; this resolves whether the floor is still
            // there when it gets there. Runs second on purpose - a direction slid along a wall can
            // just as easily end up pointing off a ledge as the original one did.
            if (TryFindSafeDirection(f, filter.Transform->Position, filter.KCC->Data.IsGrounded, direction, out FPVector2 safeDirection) == false)
            {
                // Nowhere to go that isn't a pit.
                return true;
            }

            filter.Brain->Data.Direction = safeDirection;
            filter.Brain->Data.Run = run;
            return false;
        }

        // A bot with nobody left to follow (TryResolveFollowTarget failed - no human, no other bot)
        // gets its own goal instead of standing frozen: shop during a Breathing Break, otherwise
        // hunt enemies/XP while also discovering the map. See docs/bots.md's "Solo" section.
        private static void UpdateSolo(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings)
        {
            if (f.Global->CurrentState == GameState.Breathing)
            {
                // The Store's own availability already gates on BreathingAreaSecured (see
                // PoiAvailabilityUtility.IsAvailable) - a bot that beelined for it anyway would just
                // stand there uselessly retrying TryBeginInteraction while enemies from the encounter
                // are still alive. Fight instead (no exploring - map discovery isn't the point of a
                // Breathing Break) until the area is actually secured.
                if (f.Global->BreathingAreaSecured == true)
                {
                    UpdateSoloBreathing(f, ref filter, settings);
                    return;
                }

                UpdateSoloSurvival(f, ref filter, settings, allowExploration: false);
                return;
            }

            UpdateSoloSurvival(f, ref filter, settings, allowExploration: true);
        }

        // Walks to the Store and buys one weapon, once per Breathing Break. Bypasses the normal
        // ContextInteraction/Command dance entirely (a bot has no screen to open a ChooseWindow
        // on) and calls StoreUtility's own mutators directly instead - the exact same
        // "call the utility, skip the Command" idiom LevelUpSystem.AutoPickForBots already uses via
        // LevelUpUtility.AutoConfirm. Always attempts the purchase regardless of Coin balance;
        // StoreUtility.BuyWeapon's own CoinUtility.TrySpend guard just no-ops if it can't afford it.
        private static void UpdateSoloBreathing(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings)
        {
            if (filter.Brain->StoreAttemptedAtBreathingIndex == f.Global->BreathingIndex)
                return;

            if (TryFindStore(f, out EntityRef store, out FPVector3 storePosition, out FP storeRadius) == false)
                return;

            FPVector3 selfPosition = filter.Transform->Position;
            FPVector3 delta = storePosition - selfPosition;
            delta.Y = FP._0;
            FP distance = delta.Magnitude;

            if (distance > storeRadius)
            {
                bool run = distance > Or(settings.RunDistance, DefaultRunDistance);
                EnsureRoutePath(f, ref filter, selfPosition, storePosition);
                TryMoveToward(f, ref filter, settings, selfPosition, storePosition, run);
                return;
            }

            ClearRoutePath(ref filter);
            ClearDetourPath(ref filter);
            filter.Brain->Data.Direction = default;

            StoreUtility.TryBeginInteraction(f, filter.Entity, store);

            // Not open yet (e.g. StoreConfig isn't assigned, or Availability.AvailableInBreathing
            // itself is false - UpdateSolo already confirmed BreathingAreaSecured) - leave the flag
            // unset so the bot keeps retrying next tick instead of silently skipping the Break.
            if (f.Unsafe.TryGetPointer<StoreInteraction>(filter.Entity, out var interaction) == false)
                return;

            filter.Brain->StoreAttemptedAtBreathingIndex = f.Global->BreathingIndex;

            StoreConfig config = f.FindAsset(f.RuntimeConfig.StoreConfig);
            int offerCount = StoreUtility.ResolveWeaponOfferCount(f, filter.Entity, store, config);

            for (int i = 0; i < offerCount; i++)
            {
                if (StoreUtility.IsPurchased(f, filter.Entity, store, i, isWeaponOffer: true) == true)
                    continue;

                StoreUtility.BuyWeapon(f, filter.Entity, interaction, i);
                break;
            }

            StoreUtility.Close(f, filter.Entity);
        }

        private static bool TryFindStore(Frame f, out EntityRef store, out FPVector3 position, out FP radius)
        {
            var stores = f.Filter<Store, Interactable, Transform3D>();

            while (stores.Next(out EntityRef entity, out Store _, out Interactable interactable, out Transform3D transform))
            {
                store = entity;
                position = transform.Position;
                radius = interactable.Radius;
                return true;
            }

            store = EntityRef.None;
            position = default;
            radius = default;
            return false;
        }

        // Chase the current SoloTarget (a Chunk to discover, an enemy/XP orb to reach), re-picking
        // it on a timer or the moment it stops being valid. allowExploration is false while in
        // Breathing with the area not yet secured (see UpdateSolo) - fight only, don't wander off to
        // an undiscovered chunk mid-encounter.
        private static void UpdateSoloSurvival(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, bool allowExploration)
        {
            filter.Brain->SoloRepickTimer -= f.DeltaTime;

            if (filter.Brain->SoloRepickTimer <= FP._0 || IsSoloTargetValid(f, filter.Brain->SoloTarget) == false)
            {
                RepickSoloTarget(f, ref filter, settings, allowExploration);
                ClearRoutePath(ref filter);
                ClearDetourPath(ref filter);
            }

            EntityRef target = filter.Brain->SoloTarget;

            if (target == EntityRef.None || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            FPVector3 selfPosition = filter.Transform->Position;
            FPVector3 targetPosition = targetTransform->Position;

            FP arrivalDistance = f.Has<Chunk>(target) == true
                ? SoloChunkArrivalDistance
                : f.Has<CurrencyOrb>(target) == true
                    ? SoloOrbArrivalDistance
                    : Or(settings.FollowDistance, DefaultFollowDistance);

            FPVector3 delta = targetPosition - selfPosition;
            delta.Y = FP._0;
            FP distance = delta.Magnitude;

            if (distance <= arrivalDistance)
            {
                ClearRoutePath(ref filter);
                ClearDetourPath(ref filter);
                return;
            }

            bool run = distance > Or(settings.RunDistance, DefaultRunDistance);

            // Path-FIRST for a solo goal (see EnsureRoutePath) - insist on a verified chunk route
            // before ever committing to a step toward it, rather than reactively discovering a
            // blocked/unsafe route only after already walking partway into it.
            EnsureRoutePath(f, ref filter, selfPosition, targetPosition);

            if (TryMoveToward(f, ref filter, settings, selfPosition, targetPosition, run) == false)
            {
                // Every tier failed (route + local detour - see TryFindChunkRoutePath/
                // TryFindGridPath) - give up on this goal entirely rather than leash-teleporting
                // toward it (there's no "home" position to return to while solo); the next tick
                // just picks a different one.
                filter.Brain->SoloTarget = EntityRef.None;
                ClearRoutePath(ref filter);
                ClearDetourPath(ref filter);
            }
        }

        // Makes sure a verified macro route exists before the caller ever calls TryMoveToward on a
        // STATIONARY-ish goal (a Chunk/enemy/orb/Store, all handled by solo movement) - a no-op once
        // one is already in progress. Deliberately NOT used by UpdateFollow: a live followed player
        // moves every tick, so a route computed against last tick's position would already be stale
        // by the time it mattered - that one stays reactive (TryMoveToward's own internal
        // TryFindGridPath call, only once the direct route is actually found blocked) and leans on
        // the leash as the existing backstop.
        //
        // Why this matters: SteerToward's own single-step ledge check only probes a few units ahead
        // of THIS tick's position - a straight walk toward open water can read "safe" for several
        // consecutive steps before the probe is finally close enough to the real edge to notice,
        // which is exactly the false sense of safety that let a bot wade into a lake one step at a
        // time without SteerToward ever reporting "blocked" and triggering the reactive fallback.
        //
        // TryFindChunkRoutePath (walking the level's own authored chunk-adjacency graph) is tried
        // FIRST and, if it succeeds, is the ONLY thing that populates RoutePath - a route through
        // chunks the level itself guarantees are land-connected is far more trustworthy than blindly
        // raycasting a flat grid across the whole straight-line distance with no notion of chunk
        // boundaries at all. TryFindGridPath (populating DetourPath directly, no RoutePath at all) is
        // the fallback for the same-chunk case (chunk-level routing isn't meaningful over a short
        // hop) or if chunk routing genuinely can't resolve a containing chunk for either end.
        private static void EnsureRoutePath(Frame f, ref Filter filter, FPVector3 selfPosition, FPVector3 targetPosition)
        {
            if (filter.Brain->RoutePathCursor < filter.Brain->RoutePathCount)
                return;

            if (TryFindChunkRoutePath(f, ref filter, selfPosition, targetPosition) == true)
                return;

            if (filter.Brain->DetourPathCursor >= filter.Brain->DetourPathCount)
            {
                TryFindGridPath(f, ref filter, selfPosition, targetPosition);
            }
        }

        private static void ClearDetourPath(ref Filter filter)
        {
            filter.Brain->DetourPathCount = 0;
            filter.Brain->DetourPathCursor = 0;
        }

        private static void ClearRoutePath(ref Filter filter)
        {
            filter.Brain->RoutePathCount = 0;
            filter.Brain->RoutePathCursor = 0;
        }

        // Chunk-level macro routing: BFS over Chunk.ConnectedChunks (the same land-adjacency graph
        // CollectReachableChunks/ChunkConnectivityUtility/Director spawning already trust) from the
        // bot's own chunk to the target's chunk, turning the resulting chunk SEQUENCE into one
        // RoutePath waypoint per intermediate chunk's center plus the real target position as the
        // final waypoint - so the bot actually arrives where it needs to, not just "somewhere in the
        // right chunk". A blocked LEG of this route only ever gets a local DetourPath reroute (see
        // TryMoveToward) - the rest of RoutePath is left untouched, so one pond inside one chunk
        // along the way doesn't throw out the whole macro route. Declines (returns false, no path
        // stored) when start/goal share a chunk - not a macro-routing case at all - or when either
        // position can't be resolved to a chunk; the caller (EnsureRoutePath) falls back to the
        // local grid BFS for those.
        private static bool TryFindChunkRoutePath(Frame f, ref Filter filter, FPVector3 from, FPVector3 to)
        {
            ClearRoutePath(ref filter);

            if (EnemyPathfindingUtility.TryFindContainingChunk(f, from, out EntityRef startChunk) == false
                || EnemyPathfindingUtility.TryFindContainingChunk(f, to, out EntityRef goalChunk) == false
                || startChunk == goalChunk)
            {
                return false;
            }

            var cameFrom = new Dictionary<EntityRef, EntityRef>();
            var visited = new HashSet<EntityRef> { startChunk };
            var queue = new Queue<EntityRef>();
            queue.Enqueue(startChunk);

            bool found = false;

            while (found == false && queue.Count > 0)
            {
                EntityRef current = queue.Dequeue();

                if (f.Unsafe.TryGetPointer<Chunk>(current, out var chunk) == false)
                    continue;

                for (int i = 0; i < chunk->ConnectedChunkCount && found == false; i++)
                {
                    EntityRef neighbor = chunk->ConnectedChunks[i];

                    if (visited.Add(neighbor) == false)
                        continue;

                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);

                    if (neighbor == goalChunk)
                        found = true;
                }
            }

            if (found == false)
                return false;

            // Reconstruct goal -> start via cameFrom (excluding startChunk - the bot is already
            // standing in it), then reverse into walking order. MaxRouteChunkWaypoints - 1 leaves
            // room for the final target waypoint appended below.
            var reversedChunks = new List<EntityRef>();
            EntityRef walk = goalChunk;

            while (walk != startChunk)
            {
                reversedChunks.Add(walk);

                if (reversedChunks.Count >= MaxRouteChunkWaypoints || cameFrom.TryGetValue(walk, out walk) == false)
                    return false; // longer than the buffer can hold - treat as not found
            }

            var path = filter.Brain->RoutePath;
            int count = 0;

            for (int i = reversedChunks.Count - 1; i >= 0 && count < path.Length; i--)
            {
                EntityRef chunkEntity = reversedChunks[i];

                if (f.Unsafe.TryGetPointer<Chunk>(chunkEntity, out var chunk) == false
                    || f.Unsafe.TryGetPointer<Transform3D>(chunkEntity, out var chunkTransform) == false)
                {
                    continue;
                }

                FPVector3 halfExtent = new FPVector3(chunk->ChunkSizeWidth, FP._0, chunk->ChunkSizeDepth) * FP._0_50;
                path[count] = chunkTransform->Position + halfExtent;
                count++;
            }

            // Final leg: the real target position, not just the goal chunk's center.
            if (count < path.Length)
            {
                path[count] = to;
                count++;
            }

            filter.Brain->RoutePathCount = (byte)count;
            filter.Brain->RoutePathCursor = 0;

            return count > 0;
        }

        // Brute-force local pathfinder: BFS over a grid of raycast-down samples around the straight
        // line from `from` to `to`, used only once SteerToward's direct route + 5 deflection angles
        // have already failed. Raycasts DOWN only (floor presence + a walkable height step between
        // NEIGHBORING cells) - this is about routing around VOIDS/water (this project has no
        // distinct water collider - see PlayerMovementProcessor's own comment, so a gap in the grid
        // reads exactly like one), not indoor wall collision, which SteerAroundWalls already handles
        // once the bot is actually walking a resolved waypoint. Deliberately bot-only/local: nothing
        // here is shared with or reused by any real gameplay system.
        //
        // The search area is the bounding box between from/to plus GridSearchPadding on every side,
        // coarsened (bigger GridStep) rather than grown past MaxGridCellsVisited if that box would
        // need more cells than that - a farther target gets fewer, wider steps, not more raycasts.
        private static bool TryFindGridPath(Frame f, ref Filter filter, FPVector3 from, FPVector3 to)
        {
            ClearDetourPath(ref filter);

            FP minX = FPMath.Min(from.X, to.X) - GridSearchPadding;
            FP maxX = FPMath.Max(from.X, to.X) + GridSearchPadding;
            FP minZ = FPMath.Min(from.Z, to.Z) - GridSearchPadding;
            FP maxZ = FPMath.Max(from.Z, to.Z) + GridSearchPadding;

            // Step size scales up for a farther target so the reconstructed path can't need more
            // hops than MaxGridPathWaypoints regardless of distance - most calls are now a single
            // short chunk-route LEG (see TryFindChunkRoutePath/EnsureRoutePath), but this is also the
            // fallback whenever chunk routing can't resolve a containing chunk at all, which could
            // still be a long raw distance. Chebyshev distance (max of the two axes), not Euclidean -
            // that's the real minimum hop count on an 8-connected grid. A little headroom (-4) is
            // left in the divisor for the extra hops a real detour around an obstacle needs
            // over the pure straight-line minimum.
            FP travelX = FPMath.Abs(to.X - from.X);
            FP travelZ = FPMath.Abs(to.Z - from.Z);
            FP chebyshevDistance = FPMath.Max(travelX, travelZ);
            FP minStepForBudget = chebyshevDistance / (MaxGridPathWaypoints - 4);

            FP step = FPMath.Max(GridStep, minStepForBudget);
            int width = FPMath.CeilToInt((maxX - minX) / step) + 1;
            int depth = FPMath.CeilToInt((maxZ - minZ) / step) + 1;

            // Secondary safety net - even the budget-driven step could still need too many CELLS if
            // the target is far off to one side (a long, thin bounding box), so this still coarsens
            // further rather than ever raycasting an unbounded number of cells.
            while ((long)width * depth > MaxGridCellsVisited)
            {
                step += GridStep;
                width = FPMath.CeilToInt((maxX - minX) / step) + 1;
                depth = FPMath.CeilToInt((maxZ - minZ) / step) + 1;
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            (int x, int z) startCell = (FPMath.RoundToInt((from.X - minX) / step), FPMath.RoundToInt((from.Z - minZ) / step));
            (int x, int z) goalCell = (FPMath.RoundToInt((to.X - minX) / step), FPMath.RoundToInt((to.Z - minZ) / step));

            // Same coarse cell despite SteerToward already failing - whatever's blocking is finer
            // than this grid can resolve, so there's no useful path to build here.
            if (startCell == goalCell)
                return false;

            var groundHeight = new Dictionary<(int, int), FP> { [startCell] = from.Y };
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue(startCell);

            bool found = false;

            while (found == false && queue.Count > 0)
            {
                (int x, int z) current = queue.Dequeue();
                FP currentGroundY = groundHeight[current];

                for (int dx = -1; dx <= 1 && found == false; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;

                        (int x, int z) neighbor = (current.x + dx, current.z + dz);

                        if (neighbor.x < 0 || neighbor.x >= width || neighbor.z < 0 || neighbor.z >= depth)
                            continue;

                        if (groundHeight.ContainsKey(neighbor) == true)
                            continue;

                        FPVector3 probePoint = new FPVector3(minX + step * neighbor.x, currentGroundY, minZ + step * neighbor.z);

                        if (EnemyMovementUtility.TryFindGroundHeight(f, probePoint, groundLayerMask, out FP neighborGroundY) == false)
                            continue; // void here - no floor at all within the probe

                        if (FPMath.Abs(neighborGroundY - currentGroundY) > GridStepMaxHeightDelta)
                            continue; // floor exists, but it's a disconnected level (too big a step)

                        groundHeight[neighbor] = neighborGroundY;
                        cameFrom[neighbor] = current;
                        queue.Enqueue(neighbor);

                        if (neighbor == goalCell)
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (found == false)
                return false;

            // Reconstruct goal -> start via cameFrom, then reverse into walking order.
            var reversed = new List<(int x, int z)>();
            (int x, int z) walk = goalCell;

            while (walk != startCell)
            {
                reversed.Add(walk);

                if (reversed.Count > MaxGridPathWaypoints || cameFrom.TryGetValue(walk, out walk) == false)
                    return false; // longer than the buffer can hold - treat as not found
            }

            var path = filter.Brain->DetourPath;
            int count = 0;

            for (int i = reversed.Count - 1; i >= 0 && count < path.Length; i--)
            {
                (int x, int z) cell = reversed[i];
                path[count] = new FPVector3(minX + step * cell.x, groundHeight[cell], minZ + step * cell.z);
                count++;
            }

            filter.Brain->DetourPathCount = (byte)count;
            filter.Brain->DetourPathCursor = 0;

            return count > 0;
        }

        private static bool IsSoloTargetValid(Frame f, EntityRef target)
        {
            if (target == EntityRef.None || f.Exists(target) == false)
                return false;

            // A Chunk entity never stops existing, so its own Discovered flag is what "no longer a
            // valid goal" means.
            if (f.Unsafe.TryGetPointer<Chunk>(target, out var chunk) == true)
                return chunk->Discovered == false;

            // An enemy lingers for its death animation (EnemyActionPhase.Dead) rather than being
            // destroyed the instant it dies - existence alone isn't enough, or the bot would keep
            // walking up to a corpse for that whole window instead of immediately repicking.
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
                return enemy->Phase != EnemyActionPhase.Dead;

            // An XP orb existing at all is enough (collection removes the entity).
            return true;
        }

        // Blends exploration and combat: an Elite enemy (a rare, deliberate Director spawn - see
        // docs/mortar-elite.md) always wins outright, map-wide, over everything else. Short of that,
        // while allowExploration is true AND any Chunk is still undiscovered, a coin flip decides
        // whether to prioritize discovering one or hunting an enemy/XP orb (falling back to the
        // other if its own pick comes up empty); allowExploration false (Breathing, area not yet
        // secured - see UpdateSolo) or a fully-discovered map means always hunt.
        private static void RepickSoloTarget(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, bool allowExploration)
        {
            filter.Brain->SoloTarget = EntityRef.None;
            filter.Brain->SoloRepickTimer = Or(settings.SoloRepickInterval, DefaultSoloRepickInterval);

            FPVector3 selfPosition = filter.Transform->Position;

            // Computed once and threaded through every candidate search below (see
            // CollectReachableChunks) - null (permissive) if the bot can't be resolved to a
            // containing chunk at all, rather than refusing to pick anything.
            HashSet<EntityRef> reachable = EnemyPathfindingUtility.TryFindContainingChunk(f, selfPosition, out EntityRef currentChunk) == true
                ? CollectReachableChunks(f, currentChunk)
                : null;

            if (TryFindNearestElite(f, selfPosition, reachable, out EntityRef elite) == true)
            {
                filter.Brain->SoloTarget = elite;
                return;
            }

            bool fullyDiscovered = AreAllChunksDiscovered(f);
            bool exploreFirst = allowExploration == true && fullyDiscovered == false && f.RNG->Next(FP._0, FP._1) < FP._0_50;

            if (exploreFirst == true && TryFindNearestUndiscoveredChunk(f, selfPosition, reachable, out EntityRef chunk, out _) == true)
            {
                filter.Brain->SoloTarget = chunk;
                return;
            }

            FP searchRange = Or(settings.SoloSearchRange, DefaultSoloSearchRange);

            if (TryFindNearestCombatTarget(f, selfPosition, searchRange, reachable, out EntityRef combatTarget) == true)
            {
                filter.Brain->SoloTarget = combatTarget;
                return;
            }

            if (allowExploration == true && TryFindNearestUndiscoveredChunk(f, selfPosition, reachable, out chunk, out _) == true)
            {
                filter.Brain->SoloTarget = chunk;
            }
        }

        // reachable == null means "couldn't resolve a containing chunk for the bot itself" -
        // permissive in that case (don't block picking anything just because the BOT's own position
        // is ambiguous). A candidate whose own position doesn't resolve to any chunk (e.g. genuinely
        // out of bounds) is also let through rather than excluded by default-false.
        private static bool IsPositionReachable(Frame f, HashSet<EntityRef> reachable, FPVector3 position)
        {
            if (reachable == null)
                return true;

            if (EnemyPathfindingUtility.TryFindContainingChunk(f, position, out EntityRef chunk) == false)
                return true;

            return reachable.Contains(chunk);
        }

        private static bool AreAllChunksDiscovered(Frame f)
        {
            var chunks = f.Filter<Chunk>();

            while (chunks.Next(out EntityRef _, out Chunk chunk))
            {
                if (chunk.Discovered == false)
                    return false;
            }

            return true;
        }

        // reachable == null (bot's own chunk unresolved) means unfiltered - see RepickSoloTarget.
        // Restricting to chunks actually reachable BY LAND matters because "nearest undiscovered
        // chunk" is otherwise pure straight-line distance, which happily picks a chunk on the far
        // side of a lake/void separating it from here (this project has no distinct water collider -
        // see PlayerMovementProcessor's own comment - so walking there IS walking off a real,
        // unrecoverable drop).
        private static bool TryFindNearestUndiscoveredChunk(Frame f, FPVector3 selfPosition, HashSet<EntityRef> reachable, out EntityRef chunkEntity, out FPVector3 chunkCenter)
        {
            chunkEntity = EntityRef.None;
            chunkCenter = default;
            FP bestSqrDistance = default;

            var chunks = f.Filter<Chunk, Transform3D>();

            while (chunks.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
            {
                if (chunk.Discovered == true)
                    continue;

                if (reachable != null && reachable.Contains(entity) == false)
                    continue;

                // Chunks are min-corner pivoted and never rotated (LevelGenerationSystem.
                // CommitPlacement hard-sets Rotation to identity), so the center is a plain offset -
                // same halfExtent idiom LevelGenerationSystem.TryGetLobbyStartBounds already uses.
                FPVector3 halfExtent = new FPVector3(chunk.ChunkSizeWidth, FP._0, chunk.ChunkSizeDepth) * FP._0_50;
                FPVector3 center = transform.Position + halfExtent;
                FP sqrDistance = (center - selfPosition).SqrMagnitude;

                if (chunkEntity != EntityRef.None && sqrDistance >= bestSqrDistance)
                    continue;

                chunkEntity = entity;
                chunkCenter = center;
                bestSqrDistance = sqrDistance;
            }

            return chunkEntity != EntityRef.None;
        }

        // BFS over Chunk.ConnectedChunks - the level's own land-adjacency graph, baked once by
        // LevelGenerationSystem.ComputeChunkConnectivity (the same graph Director spawning already
        // trusts via ChunkConnectivityUtility). A body of water/void between two chunks means
        // they're never linked here, so this is what tells "nearest in a straight line" apart from
        // "actually reachable on foot."
        private static HashSet<EntityRef> CollectReachableChunks(Frame f, EntityRef startChunk)
        {
            var visited = new HashSet<EntityRef> { startChunk };
            var queue = new Queue<EntityRef>();
            queue.Enqueue(startChunk);

            while (queue.Count > 0)
            {
                EntityRef current = queue.Dequeue();

                if (f.Unsafe.TryGetPointer<Chunk>(current, out var chunk) == false)
                    continue;

                for (int i = 0; i < chunk->ConnectedChunkCount; i++)
                {
                    EntityRef neighbor = chunk->ConnectedChunks[i];

                    if (visited.Add(neighbor) == true)
                        queue.Enqueue(neighbor);
                }
            }

            return visited;
        }

        // Map-wide (not range-limited like TryFindNearestCombatTarget) - an Elite is a rare,
        // deliberate Director spawn (see docs/mortar-elite.md/"Elite Territory"), not something to
        // stumble into while wandering, so a solo bot beelines for one from anywhere on the level.
        // Same Dead/Invulnerable exclusions as EnemyMovementUtility.TryFindNearestEnemy. Reachability-
        // filtered same as everything else - without it, an Elite stranded across water would get
        // re-picked immediately every time SteerToward reports it blocked, looping forever instead of
        // falling through to a normal target.
        private static bool TryFindNearestElite(Frame f, FPVector3 selfPosition, HashSet<EntityRef> reachable, out EntityRef target)
        {
            target = EntityRef.None;
            FP bestSqrDistance = default;

            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef entity, out Enemy enemy, out Transform3D transform))
            {
                if (enemy.Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(entity) == true)
                    continue;

                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);

                if (data == null || data.Tier != EnemyTier.Elite)
                    continue;

                if (IsPositionReachable(f, reachable, transform.Position) == false)
                    continue;

                FP sqrDistance = (transform.Position - selfPosition).SqrMagnitude;

                if (target != EntityRef.None && sqrDistance >= bestSqrDistance)
                    continue;

                target = entity;
                bestSqrDistance = sqrDistance;
            }

            return target != EntityRef.None;
        }

        // Nearest of {enemy, XP orb} within range - whichever is closer wins, so a bot passing an
        // orb on its way to an enemy (or vice versa) always heads for the closer one first.
        // Reachability-filtered same as the chunk/Elite searches.
        private static bool TryFindNearestCombatTarget(Frame f, FPVector3 selfPosition, FP range, HashSet<EntityRef> reachable, out EntityRef target)
        {
            target = EntityRef.None;
            FP bestSqrDistance = range * range;

            if (EnemyMovementUtility.TryFindNearestEnemy(f, selfPosition, range, out EntityRef enemy) == true
                && f.Unsafe.TryGetPointer<Transform3D>(enemy, out var enemyTransform) == true
                && IsPositionReachable(f, reachable, enemyTransform->Position) == true)
            {
                target = enemy;
                bestSqrDistance = (enemyTransform->Position - selfPosition).SqrMagnitude;
            }

            var orbs = f.Filter<CurrencyOrb, Transform3D>();

            while (orbs.Next(out EntityRef orbEntity, out CurrencyOrb orb, out Transform3D orbTransform))
            {
                if (orb.Type != CurrencyOrbType.Experience)
                    continue;

                FP sqrDistance = (orbTransform.Position - selfPosition).SqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                    continue;

                if (IsPositionReachable(f, reachable, orbTransform.Position) == false)
                    continue;

                target = orbEntity;
                bestSqrDistance = sqrDistance;
            }

            return target != EntityRef.None;
        }

        // Void avoidance. The bot follows its target in a straight line with no pathfinding, so
        // sooner or later that line points across a chasm - and the player auto-hop makes this
        // actively dangerous rather than merely clumsy: PlayerMovementProcessor reacts to "no
        // ground ahead" by JUMPING, so a bot that walks at a pit gets launched into it. That is why
        // the bot must reject the direction BEFORE auto-hop's own shorter probe ever fires, which
        // is the whole reason LedgeProbeDistance reaches further than EdgeProbeDistance.
        //
        // Tries the direct route first, then mirrored deflections out to 90 degrees - that is what
        // lets it walk ALONG the lip of a chasm toward the target instead of stopping dead at it.
        // Reports failure only when every candidate is a pit.
        private static bool TryFindSafeDirection(Frame f, FPVector3 position, bool isGrounded, FPVector2 desired, out FPVector2 safeDirection)
        {
            safeDirection = desired;

            // Airborne: these probes measure the ground under a WALKING path, which says nothing
            // useful mid-jump or mid-fall, and there is no steering decision left to protect.
            if (isGrounded == false || desired == default)
                return true;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (IsDirectionSafe(f, position, desired, groundLayerMask) == true)
                return true;

            for (int i = 0; i < LedgeDeflectionAngles.Length; i++)
            {
                FPVector2 candidate = Rotate(desired, LedgeDeflectionAngles[i]);

                if (IsDirectionSafe(f, position, candidate, groundLayerMask) == false)
                    continue;

                safeDirection = candidate;
                return true;
            }

            safeDirection = default;
            return false;
        }

        // "Safe" means one of two things, cheapest test first:
        //   1. There is ground ahead within a survivable drop - flat floor, a step down, or a real
        //      ledge the bot can walk off and live.
        //   2. Nothing at the probe point, but solid ground reappears close enough that the
        //      auto-hop clears it. This is mostly about chunk SEAMS (sub-unit gaps between placed
        //      chunks - see the project's own seam notes); without it a bot stops dead at every
        //      seam it meets, which is a far more common case than an actual chasm.
        // Anything else is a pit.
        private static bool IsDirectionSafe(Frame f, FPVector3 position, FPVector2 direction, int groundLayerMask)
        {
            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Y);

            if (EnemyMovementUtility.HasGroundAhead(f, position, flatDirection, LedgeProbeDistance, LedgeMaxDropDistance, groundLayerMask) == true)
                return true;

            if (EnemyMovementUtility.TryFindGapLanding(f, position, flatDirection, LedgeProbeDistance,
                    LedgeProbeDistance + LedgeMaxCrossableGap, LedgeGapScanStep, groundLayerMask, out FPVector3 landing) == false)
            {
                return false;
            }

            // TryFindGapLanding samples via TryFindGroundHeight, which looks 20 units down from the
            // sample - far deeper than this cares about. Without re-testing the landing HEIGHT, a
            // deep-but-floored pit would come back as a crossable gap and the bot would happily
            // walk in. Hold it to the same drop limit the ground-ahead test above uses.
            return landing.Y >= position.Y - LedgeMaxDropDistance;
        }

        // Rotates a flat (X, Z) direction around the world up axis. FPMath.SinCos rather than any
        // float trig - this runs in the simulation, so it has to be deterministic.
        private static FPVector2 Rotate(FPVector2 direction, FP degrees)
        {
            FPMath.SinCos(degrees * FP.Deg2Rad, out FP sin, out FP cos);

            return new FPVector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
        }

        private static void UpdateSkills(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FPVector3 selfPosition)
        {
            if (filter.Brain->DashSkillTimer <= FP._0)
            {
                filter.Brain->Data.DashSkill = true;
                filter.Brain->DashSkillTimer = RollDashInterval(f, settings);
            }

            // The Hero Skill button is also the interact button (see SkillSystem's own
            // ContextInteraction redirect), so what the bot does with it depends on what it's
            // standing next to.
            if (f.Unsafe.TryGetPointer<ContextInteraction>(filter.Entity, out var context) == true
                && (context->State == ContextInteractionState.Available || context->State == ContextInteractionState.NotNeeded))
            {
                // A downed teammate is the one interaction a bot SHOULD take, cooldown or not -
                // solo-testing with bots otherwise means every death waits for the area to be
                // secured (see docs/revive.md). Held rather than pulsed, because a teammate revive
                // is the one continuous-hold interaction in the game (ReviveChannelSystem reads
                // HeroSkill.IsDown every tick).
                if (context->State == ContextInteractionState.Available
                    && context->ActiveKind == InteractableKind.Revive)
                {
                    filter.Brain->Data.HeroSkill = true;
                }

                // Everything else: hold off entirely rather than have wandering bots quietly drink
                // the Healing Shrine or open the Store nobody asked for. The timer is left expired,
                // so the cast happens the moment the bot walks off it.
                return;
            }

            if (filter.Brain->HeroSkillTimer > FP._0)
                return;

            FP enemyRange = Or(settings.HeroSkillEnemyRange, DefaultHeroSkillEnemyRange);

            // Don't burn a cooldown on an empty room - a skill fired at nothing is exactly the
            // thing you were trying to watch, wasted. The timer is left expired so it casts the
            // instant something shows up.
            if (enemyRange > FP._0 && EnemyMovementUtility.TryFindNearestEnemy(f, selfPosition, enemyRange, out EntityRef _) == false)
                return;

            filter.Brain->Data.HeroSkill = true;
            filter.Brain->HeroSkillTimer = RollHeroSkillInterval(f, settings);
        }

        // Public so PlayerSpawnUtility can seed a fresh BotBrain's countdowns with the same roll
        // this system re-rolls with, rather than restating the default bands at the spawn site.
        public static FP RollHeroSkillInterval(Frame f, RuntimeConfig.BotSettings settings)
        {
            return RollInterval(f, settings.HeroSkillIntervalMin, settings.HeroSkillIntervalMax,
                DefaultHeroSkillIntervalMin, DefaultHeroSkillIntervalMax);
        }

        public static FP RollDashInterval(Frame f, RuntimeConfig.BotSettings settings)
        {
            return RollInterval(f, settings.DashIntervalMin, settings.DashIntervalMax,
                DefaultDashIntervalMin, DefaultDashIntervalMax);
        }

        // Randomized so two bots that spawned on the same tick drift apart instead of casting in
        // perfect unison. f.RNG (not a per-bot stream) - this is simulation state like any other,
        // so every client rolls the same number.
        private static FP RollInterval(Frame f, FP authoredMin, FP authoredMax, FP defaultMin, FP defaultMax)
        {
            FP min = Or(authoredMin, defaultMin);
            FP max = Or(authoredMax, defaultMax);

            return max > min ? f.RNG->Next(min, max) : min;
        }

        // Every FP in BotSettings treats 0 as "unauthored" - see that struct's own comment.
        private static FP Or(FP value, FP fallback)
        {
            return value > FP._0 ? value : fallback;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform;
            public BotBrain* Brain;

            // Every player avatar is a KCC entity (see PlayerMovementProcessor) - held here for the
            // leash teleport and for the grounded check the ledge probes are gated on.
            public KCC* KCC;
        }
    }
}
