namespace Quantum
{
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

        // Spread: each bot parks a little further out than the one before it, keyed off its own
        // player slot, so two bots following the same person don't grind against each other on top
        // of the target. Cheap stand-in for real formation slots.
        private static readonly FP PerBotSpread = FP._0_50 + FP._0_25;

        public override void Update(Frame f, ref Filter filter)
        {
            RuntimeConfig.BotSettings settings = f.RuntimeConfig.Bots;

            // Captured before the clear below - the follow hysteresis needs to know whether the
            // bot was moving LAST tick (see UpdateFollow).
            bool wasMoving = filter.Brain->Data.Direction != default;

            // Cleared every tick, so every button written below is a genuine one-tick pulse and a
            // WasPressed consumer can never see the same decision twice.
            filter.Brain->Data = default;

            TickTimers(f, filter.Brain);

            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
                return;

            if (TryResolveFollowTarget(f, filter.Entity, out EntityRef target, out FPVector3 targetPosition) == false)
                return;

            FPVector3 selfPosition = filter.Transform->Position;
            FPVector3 delta = targetPosition - selfPosition;
            delta.Y = FP._0;
            FP distance = delta.Magnitude;
            bool targetIncapacitated = PlayerLifeStateUtility.IsIncapacitated(f, target);

            // Follow first, leash second: UpdateFollow is what discovers that every route to the
            // target runs off a ledge, and a bot pinned at the lip of a chasm needs the leash even
            // when the target is close enough that distance alone would never trigger it.
            bool blocked = UpdateFollow(f, ref filter, settings, delta, distance, wasMoving, targetIncapacitated);

            UpdateLeash(f, ref filter, settings, targetPosition, distance, blocked);
            UpdateSkills(f, ref filter, settings, selfPosition);
        }

        private static void TickTimers(Frame f, BotBrain* brain)
        {
            brain->HeroSkillTimer -= f.DeltaTime;
            brain->DashSkillTimer -= f.DeltaTime;
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
        // that is what promotes it to "stranded" for the leash (see UpdateLeash). Standing still
        // because it is already close enough is not blocked.
        private static bool UpdateFollow(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FPVector3 delta, FP distance, bool wasMoving, bool targetIncapacitated)
        {
            FP followDistance = targetIncapacitated == true
                ? DownedTargetFollowDistance
                : Or(settings.FollowDistance, DefaultFollowDistance) + ResolveSpread(filter.PlayerLink->Player);
            FP slack = Or(settings.FollowSlack, DefaultFollowSlack);

            // Hysteresis: a bot already walking closes all the way to followDistance, but one
            // that has parked won't set off again until the target is a full slack further out.
            // Without it a bot sitting exactly at followDistance stutters in and out of motion
            // every single tick.
            FP resumeDistance = wasMoving == true ? followDistance : followDistance + slack;

            if (distance <= resumeDistance || distance <= FP._0)
                return false;

            FPVector2 direction = new FPVector2(delta.X, delta.Z).Normalized;

            // Same wall deflection an AvoidWalls enemy gets - turns "grinding into the corner
            // between here and the player" into "sliding along it", which is most of what keeps a
            // pathfinding-free follow usable inside chunk geometry.
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
                // Nowhere to go that isn't a pit. Stand still rather than walk in - the leash is
                // what eventually recovers the bot from here (see UpdateLeash's "blocked" case).
                return true;
            }

            filter.Brain->Data.Direction = safeDirection;
            filter.Brain->Data.Run = distance > Or(settings.RunDistance, DefaultRunDistance);
            return false;
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

        // Each bot parks PerBotSpread further out than the previous player slot, so a party of them
        // fans out behind the target instead of stacking.
        private static FP ResolveSpread(PlayerRef player)
        {
            int index = (int)player;
            return index > 0 ? index * PerBotSpread : FP._0;
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
            public PlayerLink* PlayerLink;

            // Every player avatar is a KCC entity (see PlayerMovementProcessor) - held here for the
            // leash teleport and for the grounded check the ledge probes are gated on.
            public KCC* KCC;
        }
    }
}
