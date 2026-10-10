namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Co-op testing autopilot (see docs/bots.md) - drives every entity with a BotBrain: spawned
    // bots (RuntimePlayer.IsBot) and humans who pressed the Become Bot cheat. Writes the same Input
    // struct a human does, so PlayerMovementProcessor, SkillSystem etc. treat it like any player;
    // firing is auto-attack off Aim.Target, so Input.Fire is never written.
    //
    // Each tick: perceive (BotPerception), pick ONE goal (SelectGoal), execute it (movement through
    // BotNavigation), then decide skills (BotSkillUtility). Goals, highest priority first:
    //   Revive   - a Downed teammate nobody else is reviving.
    //   Interact - Breathing Break, area secured, nothing close: Store/Blacksmith/Shrine/Cursed Rift.
    //   Retreat  - low Health with enemies close: back away (toward the leader if there is one).
    //   Follow   - regrouping: the leader got further than LeaderMaxDistance.
    //   Loot     - a pickup right next to the bot and no enemy on top of it.
    //   Fight    - an enemy in EngageRange (and, with a leader, inside the leader's tether): hold
    //              the weapon's preferred range and strafe.
    //   Loot     - any orb/Chest in LootRange (inside the leader's tether).
    //   Recover  - a landed DroppedAccessory (own, or a teammate's with ally recovery on) within 30,
    //              and - with a leader - well inside LeaderMaxDistance of them.
    //   Follow   - formation slot behind/beside the leader.
    //   Explore  - no leader at all: Elites, undiscovered chunks, then any enemy, map-wide.
    // The leader is the lowest-PlayerRef HUMAN (no BotBrain) - with one, the bot plays the game but
    // stays around them; without one it plays the whole map on its own.
    //
    // Runs inside GameplaySystemGroup immediately before KCCSystem, so this tick's decision is the
    // one this tick's movement resolves, and the bot freezes with everyone while a screen is open.
    [Preserve]
    public unsafe class BotInputSystem : SystemMainThreadFilter<BotInputSystem.Filter>
    {
        // Fallbacks for an unauthored (all-zero) RuntimeConfig.Bots - a struct can't carry field
        // initializers, so every FP there means "0 = use this default" (see Or).
        private static readonly FP DefaultFollowDistance = 3;
        private static readonly FP DefaultFollowSlack = FP._1 + FP._0_50;
        private static readonly FP DefaultRunDistance = 6;
        private static readonly FP DefaultLeashDistance = 25;
        private static readonly FP DefaultLeashTimeout = 3;
        private static readonly FP DefaultLeaderMaxDistance = 14;
        private static readonly FP DefaultLeaderTetherRadius = 12;
        private static readonly FP DefaultEngageRange = 11;
        private static readonly FP DefaultLootRange = 9;
        private static readonly FP DefaultRetreatHealthFraction = FP.FromString("0.35");
        private static readonly FP DefaultFormationOffsetMin = FP._2 + FP._0_50;
        private static readonly FP DefaultFormationOffsetMax = FP._3 + FP._0_50;
        private static readonly FP DefaultFormationRerollIntervalMin = 15;
        private static readonly FP DefaultFormationRerollIntervalMax = 25;

        // Formation sides, relative to the leader's travel direction (180 = directly behind),
        // assigned by the bot's index among bots so two bots never share a side. A reroll only
        // jitters around its side by FormationJitter.
        private static readonly FP[] FormationBaseAngles = { 145, 215, 100, 260 };
        private static readonly FP FormationJitter = 15;

        // How close counts as "arrived" at each kind of goal.
        private static readonly FP SlotArrivalDistance = FP._1 + FP._0_25;
        private static readonly FP PickupArrivalDistance = FP._0_25;
        private static readonly FP ChunkArrivalDistance = FP._1;
        private static readonly FP ReviveArrivalDistance = FP._0_50 + FP._0_25;

        // Loot right next to the bot beats fighting, as long as no enemy is this close.
        private static readonly FP CloseLootRange = 4;
        private static readonly FP CloseLootEnemyClearance = 3;

        // An enemy this close is fought even if it's outside the leader's tether - it's attacking.
        private static readonly FP SelfDefenseRange = 4;

        // Downed teammates further than this aren't worth crossing the map for.
        private static readonly FP ReviveSearchRange = 40;

        // Breathing POIs are only visited with no enemy within this range.
        private static readonly FP InteractEnemyClearance = 6;

        // Fight positioning: preferred distance is this fraction of the weapon's range, clamped.
        private static readonly FP FightRangeFraction = FP.FromString("0.6");
        private static readonly FP FightMinDistance = FP._1 + FP._0_50;
        private static readonly FP FightMaxDistance = 8;
        private static readonly FP FightDistanceTolerance = FP._1 + FP._0_50;
        private static readonly FP DefaultWeaponRange = 6;

        // A held Fight target is re-evaluated after this long, so a better target (an Elite) can win.
        private static readonly FP FightRetargetInterval = 3;
        private static readonly FP ExploreRepickInterval = 10;
        private static readonly FP LootGiveUpTime = 6;

        // Dropped accessories: how far from the bot it'll walk for one, and - with a leader - it must
        // also lie this much inside LeaderMaxDistance of them, or fetching it would just trip the
        // regroup and the bot would bounce between the two. A dropped accessory never expires, so
        // "eventually" is fine: once the leader drifts near it, the bot fetches it.
        private static readonly FP AccessoryRecoverRange = 30;
        private static readonly FP AccessoryLeaderMargin = 2;
        private static readonly FP AccessoryGiveUpTime = 10;

        // Stuck: wanted to move but stayed within StuckRadius for StuckTimeout -> drop the target
        // (ignored for IgnoreDuration) or, while following, count as stranded for the leash.
        private static readonly FP StuckRadius = FP._1;
        private static readonly FP StuckTimeout = FP._2 + FP._0_50;
        private static readonly FP FollowStrandedStuckTime = FP._1 + FP._0_50;
        private static readonly FP IgnoreDuration = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            RuntimeConfig.BotSettings settings = f.RuntimeConfig.Bots;
            BotBrain* brain = filter.Brain;

            // Captured before the clear - the follow hysteresis needs to know whether the bot was
            // moving LAST tick.
            bool wasMoving = brain->Data.Direction != default;

            // Cleared every tick, so every button written below is a one-tick pulse.
            brain->Data = default;

            TickTimers(f, brain);

            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
            {
                SetGoal(brain, filter.Transform->Position, BotGoal.None, EntityRef.None);
                brain->Heading = default;
                return;
            }

            // Mid-revive: ReviveChannelSystem reads Input.HeroSkill.IsDown every tick - keep holding
            // and stand still (the channel already slows movement) until it completes or breaks.
            if (f.Has<ReviveChannel>(filter.Entity) == true)
            {
                brain->Data.HeroSkill = true;
                brain->Heading = default;
                return;
            }

            FPVector3 position = filter.Transform->Position;
            BotPerception perception = BotPerception.Gather(f, filter.Entity, position);

            bool hasLeader = TryResolveLeader(f, filter.Entity, out EntityRef leader, out FPVector3 leaderPosition);
            FP leaderDistance = hasLeader == true ? BotNavigation.FlatDistance(position, leaderPosition) : FP._0;

            if (hasLeader == true)
            {
                UpdateLeaderHeading(brain, leaderPosition);
                UpdateRegrouping(brain, settings, leaderDistance);
            }
            else
            {
                brain->Regrouping = false;
            }

            SelectGoal(f, ref filter, settings, in perception, hasLeader, leaderPosition);

            switch (brain->Goal)
            {
                case BotGoal.Revive:
                    ExecuteRevive(f, ref filter, settings);
                    break;

                case BotGoal.Interact:
                    ExecuteInteract(f, ref filter, settings);
                    break;

                case BotGoal.Retreat:
                    ExecuteRetreat(f, ref filter, in perception, hasLeader, leaderPosition);
                    break;

                case BotGoal.Fight:
                    ExecuteFight(f, ref filter, settings, in perception);
                    break;

                case BotGoal.Loot:
                    ExecuteMoveTo(f, ref filter, settings, PickupArrivalDistance);
                    break;

                case BotGoal.Recover:
                    ExecuteMoveTo(f, ref filter, settings, PickupArrivalDistance);
                    break;

                case BotGoal.Explore:
                    ExecuteExplore(f, ref filter, settings);
                    break;

                case BotGoal.Follow:
                    ExecuteFollow(f, ref filter, settings, leader, leaderPosition, leaderDistance, wasMoving);
                    break;
            }

            bool catchUp = hasLeader == true && brain->Goal == BotGoal.Follow && brain->Regrouping == true
                && BotSkillUtility.ShouldCatchUp(leaderDistance);

            BotSkillUtility.Update(f, ref filter, in perception, settings, catchUp);

            UpdateStuck(f, ref filter);

            if (brain->Data.Direction == default)
            {
                brain->Heading = default;
            }
        }

        private static void TickTimers(Frame f, BotBrain* brain)
        {
            brain->GoalTimer += f.DeltaTime;
            brain->FormationRerollTimer -= f.DeltaTime;
            brain->StrafeTimer -= f.DeltaTime;

            if (brain->IgnoreTimer > FP._0)
            {
                brain->IgnoreTimer -= f.DeltaTime;

                if (brain->IgnoreTimer <= FP._0)
                {
                    brain->IgnoredTarget = EntityRef.None;
                }
            }
        }

        // ---------------------------------------------------------------- goal selection

        private static void SelectGoal(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, in BotPerception perception, bool hasLeader, FPVector3 leaderPosition)
        {
            BotBrain* brain = filter.Brain;
            FPVector3 position = perception.Position;

            if (TryFindDownedTeammate(f, filter.Entity, brain, position, out EntityRef downed) == true)
            {
                SetGoal(brain, position, BotGoal.Revive, downed);
                return;
            }

            if (TrySelectInteract(f, ref filter, settings, in perception, out EntityRef poi) == true)
            {
                SetGoal(brain, position, BotGoal.Interact, poi);
                return;
            }

            if (perception.HealthFraction < RetreatFraction(settings) && perception.NearestEnemyDistance < BotPerception.ThreatRadius)
            {
                SetGoal(brain, position, BotGoal.Retreat, EntityRef.None);
                return;
            }

            if (hasLeader == true && brain->Regrouping == true)
            {
                SetGoal(brain, position, BotGoal.Follow, EntityRef.None);
                return;
            }

            if (perception.NearestEnemyDistance > CloseLootEnemyClearance
                && TrySelectLoot(f, brain, position, CloseLootRange, hasLeader, leaderPosition, settings, out EntityRef closeLoot) == true)
            {
                SetGoal(brain, position, BotGoal.Loot, closeLoot);
                return;
            }

            if (TrySelectFightTarget(f, brain, settings, in perception, hasLeader, leaderPosition, out EntityRef enemy) == true)
            {
                SetGoal(brain, position, BotGoal.Fight, enemy);
                return;
            }

            if (TrySelectLoot(f, brain, position, Or(settings.LootRange, DefaultLootRange), hasLeader, leaderPosition, settings, out EntityRef loot) == true)
            {
                SetGoal(brain, position, BotGoal.Loot, loot);
                return;
            }

            if (TrySelectAccessory(f, filter.Entity, brain, position, hasLeader, leaderPosition, settings, out EntityRef accessory) == true)
            {
                SetGoal(brain, position, BotGoal.Recover, accessory);
                return;
            }

            if (hasLeader == true)
            {
                SetGoal(brain, position, BotGoal.Follow, EntityRef.None);
                return;
            }

            if (TrySelectExplore(f, brain, position, out EntityRef exploreTarget) == true)
            {
                SetGoal(brain, position, BotGoal.Explore, exploreTarget);
                return;
            }

            SetGoal(brain, position, BotGoal.None, EntityRef.None);
        }

        // A goal or target change resets everything tied to the old one - paths, stuck tracking,
        // the goal timer - so nothing carries over to the new destination.
        private static void SetGoal(BotBrain* brain, FPVector3 position, BotGoal goal, EntityRef target)
        {
            if (brain->Goal == goal && brain->GoalTarget == target)
                return;

            brain->Goal = goal;
            brain->GoalTarget = target;
            brain->GoalTimer = FP._0;
            brain->StuckTimer = FP._0;
            brain->ProgressAnchor = position;
            brain->LeashTimer = FP._0;
            BotNavigation.ClearPaths(brain);
        }

        // The lowest-PlayerRef human who isn't KO (a dead end - see docs/revive.md). A Downed leader
        // is still the leader; Revive outranks following anyway.
        private static bool TryResolveLeader(Frame f, EntityRef self, out EntityRef leader, out FPVector3 position)
        {
            leader = EntityRef.None;
            position = default;
            int bestPlayer = int.MaxValue;

            var players = f.Filter<PlayerLink, Transform3D>();

            while (players.Next(out EntityRef entity, out PlayerLink playerLink, out Transform3D transform) == true)
            {
                if (entity == self || f.Has<BotBrain>(entity) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<PlayerLifeState>(entity, out var lifeState) == true && lifeState->State == PlayerLifeStateKind.KO)
                    continue;

                int player = (int)playerLink.Player;

                if (player >= bestPlayer)
                    continue;

                leader = entity;
                bestPlayer = player;
                position = transform.Position;
            }

            return leader != EntityRef.None;
        }

        // The leader's smoothed TRAVEL direction - formation slots hang off this rather than
        // Aim.Angle, which swings with every auto-aim retarget and used to drag bots around in arcs.
        private static void UpdateLeaderHeading(BotBrain* brain, FPVector3 leaderPosition)
        {
            FPVector3 delta = leaderPosition - brain->LastLeaderPosition;
            brain->LastLeaderPosition = leaderPosition;

            FPVector2 step = new FPVector2(delta.X, delta.Z);
            FP stepLength = step.Magnitude;

            // Ignore standing still (noise) and teleports (respawn, leash, Store warp).
            if (stepLength < FP._0_01 || stepLength > FP._3)
                return;

            FPVector2 direction = step / stepLength;

            if (brain->LeaderHeading == default)
            {
                brain->LeaderHeading = direction;
                return;
            }

            FPVector2 blended = brain->LeaderHeading * FP.FromString("0.92") + direction * FP.FromString("0.08");
            brain->LeaderHeading = blended.SqrMagnitude > FP._0_01 ? blended.Normalized : direction;
        }

        private static void UpdateRegrouping(BotBrain* brain, RuntimeConfig.BotSettings settings, FP leaderDistance)
        {
            if (leaderDistance > Or(settings.LeaderMaxDistance, DefaultLeaderMaxDistance))
            {
                brain->Regrouping = true;
            }
            else if (leaderDistance <= Or(settings.FollowDistance, DefaultFollowDistance) + Or(settings.FollowSlack, DefaultFollowSlack))
            {
                brain->Regrouping = false;
            }
        }

        private static bool TryFindDownedTeammate(Frame f, EntityRef self, BotBrain* brain, FPVector3 position, out EntityRef downed)
        {
            downed = EntityRef.None;
            FP bestDistance = ReviveSearchRange;

            var players = f.Filter<PlayerLink, PlayerLifeState, Transform3D>();

            while (players.Next(out EntityRef entity, out PlayerLink _, out PlayerLifeState lifeState, out Transform3D transform) == true)
            {
                if (entity == self || entity == brain->IgnoredTarget || lifeState.State != PlayerLifeStateKind.Downed)
                    continue;

                // Someone else is already holding - don't crowd them.
                if (lifeState.ReviveHolder != EntityRef.None && lifeState.ReviveHolder != self)
                    continue;

                FP distance = BotNavigation.FlatDistance(position, transform.Position);

                if (distance >= bestDistance)
                    continue;

                downed = entity;
                bestDistance = distance;
            }

            return downed != EntityRef.None;
        }

        private static bool TrySelectInteract(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, in BotPerception perception, out EntityRef poi)
        {
            poi = EntityRef.None;

            if (f.Global->CurrentPhaseKind != SurvivalPhaseKind.Breathing || f.Global->BreathingAreaSecured == false)
                return false;

            if (perception.NearestEnemyDistance < InteractEnemyClearance)
                return false;

            BotBrain* brain = filter.Brain;

            // Sticky - only re-picked once the held POI is used or stops being wanted.
            if (brain->Goal == BotGoal.Interact
                && BotPoiUtility.IsStillWanted(f, filter.Entity, brain, settings, perception.HealthFraction, brain->GoalTarget) == true)
            {
                poi = brain->GoalTarget;
                return true;
            }

            HashSet<EntityRef> reachable = BotNavigation.CollectReachableFrom(f, perception.Position);

            return BotPoiUtility.TryPick(f, filter.Entity, brain, settings, perception.HealthFraction, reachable, perception.Position, out poi);
        }

        private static bool TrySelectFightTarget(Frame f, BotBrain* brain, RuntimeConfig.BotSettings settings, in BotPerception perception,
            bool hasLeader, FPVector3 leaderPosition, out EntityRef target)
        {
            target = EntityRef.None;

            if (perception.HasEnemies == false)
                return false;

            FP engageRange = Or(settings.EngageRange, DefaultEngageRange);
            FP tether = Or(settings.LeaderTetherRadius, DefaultLeaderTetherRadius);
            var enemies = perception.Enemies;

            // Keep the current target for a while - switching every tick to whichever enemy is a
            // hair closer is what makes a bot jitter between two of them.
            if (brain->Goal == BotGoal.Fight && brain->GoalTimer < FightRetargetInterval)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].Entity == brain->GoalTarget && enemies[i].Distance <= engageRange + FP._3)
                    {
                        target = brain->GoalTarget;
                        return true;
                    }
                }
            }

            FP bestScore = FP.UseableMax;

            for (int i = 0; i < enemies.Count; i++)
            {
                BotPerception.EnemyInfo enemy = enemies[i];

                if (enemy.Entity == brain->IgnoredTarget)
                    continue;

                bool selfDefense = enemy.Distance <= SelfDefenseRange;

                if (selfDefense == false)
                {
                    if (enemy.Distance > engageRange)
                        continue;

                    if (hasLeader == true && BotNavigation.FlatDistance(leaderPosition, enemy.Position) > tether)
                        continue;
                }

                // Elites first (a big flat bonus), then nearest.
                FP score = enemy.Distance - (enemy.IsElite == true ? 100 : 0);

                if (score >= bestScore)
                    continue;

                target = enemy.Entity;
                bestScore = score;
            }

            return target != EntityRef.None;
        }

        // Nearest orb (XP/Coin/Rift Shard) or unopened Chest within `range` of the bot - and of the
        // leader, when there is one.
        private static bool TrySelectLoot(Frame f, BotBrain* brain, FPVector3 position, FP range, bool hasLeader, FPVector3 leaderPosition,
            RuntimeConfig.BotSettings settings, out EntityRef target)
        {
            target = EntityRef.None;
            FP tether = Or(settings.LeaderTetherRadius, DefaultLeaderTetherRadius);

            if (brain->Goal == BotGoal.Loot && IsLootValid(f, brain->GoalTarget) == true)
            {
                // Standing on it for this long and it still exists - something about it can't be
                // collected (out of reach above the floor, a Chest gated by something else). Move on.
                if (brain->GoalTimer > LootGiveUpTime)
                {
                    brain->IgnoredTarget = brain->GoalTarget;
                    brain->IgnoreTimer = IgnoreDuration;
                }
                else if (f.Unsafe.TryGetPointer<Transform3D>(brain->GoalTarget, out var held) == true
                    && BotNavigation.FlatDistance(position, held->Position) <= range + FP._3)
                {
                    target = brain->GoalTarget;
                    return true;
                }
            }

            FP bestDistance = range;

            var orbs = f.Filter<CurrencyOrb, Transform3D>();

            while (orbs.Next(out EntityRef entity, out CurrencyOrb _, out Transform3D transform) == true)
            {
                ConsiderLoot(entity, transform.Position, position, hasLeader, leaderPosition, tether, brain, ref target, ref bestDistance);
            }

            var chests = f.Filter<Chest, Transform3D>();

            while (chests.Next(out EntityRef entity, out Chest chest, out Transform3D transform) == true)
            {
                if (chest.Opened == true)
                    continue;

                ConsiderLoot(entity, transform.Position, position, hasLeader, leaderPosition, tether, brain, ref target, ref bestDistance);
            }

            return target != EntityRef.None;
        }

        private static void ConsiderLoot(EntityRef entity, FPVector3 lootPosition, FPVector3 position, bool hasLeader, FPVector3 leaderPosition, FP tether,
            BotBrain* brain, ref EntityRef target, ref FP bestDistance)
        {
            if (entity == brain->IgnoredTarget)
                return;

            FP distance = BotNavigation.FlatDistance(position, lootPosition);

            if (distance >= bestDistance)
                return;

            if (hasLeader == true && BotNavigation.FlatDistance(leaderPosition, lootPosition) > tether)
                return;

            target = entity;
            bestDistance = distance;
        }

        // A landed, recoverable accessory - this bot's own, or (AccessoryGuardConfig.AllowAllyRecovery)
        // any teammate's; pickup returns it to its owner either way (AccessoryGuardSystem). Debris
        // from a break (DroppedAccessory.Broken) and one still in the air (owner's guard Airborne)
        // aren't collectible. Nearest wins; the held one is kept until collected or given up on.
        private static bool TrySelectAccessory(Frame f, EntityRef self, BotBrain* brain, FPVector3 position, bool hasLeader, FPVector3 leaderPosition,
            RuntimeConfig.BotSettings settings, out EntityRef target)
        {
            target = EntityRef.None;

            bool allowAllies = false;

            if (f.RuntimeConfig.AccessoryGuardConfig.IsValid == true)
            {
                allowAllies = f.FindAsset(f.RuntimeConfig.AccessoryGuardConfig).AllowAllyRecovery;
            }

            FP leaderLimit = Or(settings.LeaderMaxDistance, DefaultLeaderMaxDistance) - AccessoryLeaderMargin;

            if (brain->Goal == BotGoal.Recover && IsAccessoryRecoverable(f, self, brain->GoalTarget, allowAllies) == true
                && f.Unsafe.TryGetPointer<Transform3D>(brain->GoalTarget, out var held) == true)
            {
                // Standing on it this long and it's still there - can't actually be collected from
                // here (landed somewhere the bot's position never overlaps). Move on for a while.
                if (brain->GoalTimer > AccessoryGiveUpTime && BotNavigation.FlatDistance(position, held->Position) < FP._1 + FP._0_50)
                {
                    brain->IgnoredTarget = brain->GoalTarget;
                    brain->IgnoreTimer = IgnoreDuration;
                }
                else if (hasLeader == false || BotNavigation.FlatDistance(leaderPosition, held->Position) <= leaderLimit)
                {
                    target = brain->GoalTarget;
                    return true;
                }
            }

            FP bestDistance = AccessoryRecoverRange;
            var accessories = f.Filter<DroppedAccessory, Transform3D>();

            while (accessories.Next(out EntityRef entity, out DroppedAccessory _, out Transform3D transform) == true)
            {
                if (entity == brain->IgnoredTarget || IsAccessoryRecoverable(f, self, entity, allowAllies) == false)
                    continue;

                if (hasLeader == true && BotNavigation.FlatDistance(leaderPosition, transform.Position) > leaderLimit)
                    continue;

                FP distance = BotNavigation.FlatDistance(position, transform.Position);

                if (distance >= bestDistance)
                    continue;

                target = entity;
                bestDistance = distance;
            }

            return target != EntityRef.None;
        }

        private static bool IsAccessoryRecoverable(Frame f, EntityRef self, EntityRef accessory, bool allowAllies)
        {
            if (accessory == EntityRef.None || f.Unsafe.TryGetPointer<DroppedAccessory>(accessory, out var dropped) == false || dropped->Broken == true)
                return false;

            if (dropped->Owner != self && allowAllies == false)
                return false;

            // Landed = the owner's guard is tracking THIS entity as Dropped (Airborne while it flies).
            return f.Unsafe.TryGetPointer<AccessoryGuard>(dropped->Owner, out var guard) == true
                && guard->State == AccessoryGuardState.Dropped
                && guard->Accessory == accessory;
        }

        private static bool IsLootValid(Frame f, EntityRef target)
        {
            if (target == EntityRef.None || f.Exists(target) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Chest>(target, out var chest) == true)
                return chest->Opened == false;

            return f.Has<CurrencyOrb>(target);
        }

        // No leader: an Elite anywhere reachable, else the nearest undiscovered reachable chunk, else
        // the nearest reachable enemy anywhere. Sticky until invalid or ExploreRepickInterval.
        private static bool TrySelectExplore(Frame f, BotBrain* brain, FPVector3 position, out EntityRef target)
        {
            if (brain->Goal == BotGoal.Explore && brain->GoalTimer < ExploreRepickInterval && IsExploreTargetValid(f, brain->GoalTarget) == true)
            {
                target = brain->GoalTarget;
                return true;
            }

            HashSet<EntityRef> reachable = BotNavigation.CollectReachableFrom(f, position);

            if (TryFindNearestEnemyMapWide(f, brain, position, reachable, elitesOnly: true, out target) == true)
                return true;

            if (TryFindNearestUndiscoveredChunk(f, brain, position, reachable, out target) == true)
                return true;

            return TryFindNearestEnemyMapWide(f, brain, position, reachable, elitesOnly: false, out target);
        }

        private static bool IsExploreTargetValid(Frame f, EntityRef target)
        {
            if (target == EntityRef.None || f.Exists(target) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Chunk>(target, out var chunk) == true)
                return chunk->Discovered == false;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
                return enemy->Phase != EnemyActionPhase.Dead;

            return false;
        }

        private static bool TryFindNearestEnemyMapWide(Frame f, BotBrain* brain, FPVector3 position, HashSet<EntityRef> reachable, bool elitesOnly, out EntityRef target)
        {
            target = EntityRef.None;
            FP bestSqrDistance = FP.UseableMax;

            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef entity, out Enemy enemy, out Transform3D transform) == true)
            {
                if (entity == brain->IgnoredTarget || enemy.Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(entity) == true)
                    continue;

                if (elitesOnly == true)
                {
                    EnemyDataAsset data = f.FindAsset(enemy.EnemyData);

                    if (data == null || (data.Tier != EnemyTier.Elite && data.Tier != EnemyTier.Boss))
                        continue;
                }

                FP sqrDistance = (transform.Position - position).SqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                    continue;

                if (BotNavigation.IsPositionReachable(f, reachable, transform.Position) == false)
                    continue;

                target = entity;
                bestSqrDistance = sqrDistance;
            }

            return target != EntityRef.None;
        }

        private static bool TryFindNearestUndiscoveredChunk(Frame f, BotBrain* brain, FPVector3 position, HashSet<EntityRef> reachable, out EntityRef target)
        {
            target = EntityRef.None;
            FP bestSqrDistance = FP.UseableMax;

            var chunks = f.Filter<Chunk>();

            while (chunks.Next(out EntityRef entity, out Chunk chunk) == true)
            {
                if (chunk.Discovered == true || entity == brain->IgnoredTarget)
                    continue;

                if (reachable != null && reachable.Contains(entity) == false)
                    continue;

                if (BotNavigation.TryGetChunkCenter(f, entity, out FPVector3 center) == false)
                    continue;

                FP sqrDistance = (center - position).SqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                    continue;

                target = entity;
                bestSqrDistance = sqrDistance;
            }

            return target != EntityRef.None;
        }

        // ---------------------------------------------------------------- goal execution

        private static void ExecuteRevive(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings)
        {
            BotBrain* brain = filter.Brain;

            // In range: ContextInteractionSystem already resolved this teammate as the Available
            // Revive target - hold the button (SkillSystem's redirect starts the channel) and stop.
            if (f.Unsafe.TryGetPointer<ContextInteraction>(filter.Entity, out var context) == true
                && context->State == ContextInteractionState.Available
                && context->ActiveKind == InteractableKind.Revive)
            {
                brain->Data.HeroSkill = true;
                return;
            }

            ExecuteMoveTo(f, ref filter, settings, ReviveArrivalDistance);
        }

        private static void ExecuteInteract(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings)
        {
            BotBrain* brain = filter.Brain;
            EntityRef poi = brain->GoalTarget;

            if (f.Unsafe.TryGetPointer<Transform3D>(poi, out var poiTransform) == false)
                return;

            FP arrival = BotPoiUtility.ResolveArrivalDistance(f, poi);

            if (BotNavigation.FlatDistance(filter.Transform->Position, poiTransform->Position) > arrival)
            {
                ExecuteMoveTo(f, ref filter, settings, arrival);
                return;
            }

            BotPoiUtility.Use(f, filter.Entity, brain, poi);
            SetGoal(brain, filter.Transform->Position, BotGoal.None, EntityRef.None);
        }

        // Away from the enemies, pulled toward the leader when there is one (the safest place in
        // co-op is next to your teammate). Falls back to sidestepping if straight back is a ledge.
        private static void ExecuteRetreat(Frame f, ref Filter filter, in BotPerception perception, bool hasLeader, FPVector3 leaderPosition)
        {
            FPVector2 direction = perception.ThreatDirection;

            if (hasLeader == true)
            {
                FPVector3 toLeader = leaderPosition - perception.Position;
                FPVector2 toLeaderFlat = new FPVector2(toLeader.X, toLeader.Z);

                if (toLeaderFlat.SqrMagnitude > FP._1)
                {
                    direction += toLeaderFlat.Normalized * FP._0_50;
                }
            }

            if (direction == default)
                return;

            FPVector3 delta = new FPVector3(direction.X, FP._0, direction.Y);

            if (BotNavigation.SteerToward(f, ref filter, delta, run: true) == true)
                return;

            FPVector2 side = BotNavigation.Rotate(direction.Normalized, 90 * (filter.Brain->DeflectSide >= 0 ? 1 : -1));
            BotNavigation.SteerToward(f, ref filter, new FPVector3(side.X, FP._0, side.Y), run: true);
        }

        // Hold the weapon's preferred distance from the target and strafe around it. Brute charges
        // straight in while Juggernaut is active (his damage lands on contact).
        private static void ExecuteFight(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, in BotPerception perception)
        {
            BotBrain* brain = filter.Brain;

            if (f.Unsafe.TryGetPointer<Transform3D>(brain->GoalTarget, out var targetTransform) == false)
                return;

            FPVector3 position = perception.Position;
            FPVector3 targetPosition = targetTransform->Position;
            FPVector3 toTarget = targetPosition - position;
            toTarget.Y = FP._0;
            FP distance = toTarget.Magnitude;

            FP preferred = ResolvePreferredFightDistance(f, filter.Entity);

            if (brain->StrafeTimer <= FP._0 || brain->StrafeSign == 0)
            {
                brain->StrafeSign = f.RNG->Next(0, 2) == 0 ? -1 : 1;
                brain->StrafeTimer = f.RNG->Next(FP._1 + FP._0_50, FP._3);
            }

            if (distance > preferred + FightDistanceTolerance)
            {
                bool run = distance > Or(settings.RunDistance, DefaultRunDistance);

                if (BotNavigation.TryMoveToward(f, ref filter, targetPosition, run) == false)
                {
                    IgnoreCurrentTarget(brain, position);
                }

                return;
            }

            if (distance <= FP._0_01)
                return;

            FPVector3 radial = toTarget / distance;

            // Too close for a ranged weapon: back off (away from the whole crowd, not just this one).
            if (distance < preferred - FightDistanceTolerance && preferred > FP._2 + FP._0_50)
            {
                FPVector3 away = perception.ThreatDirection != default
                    ? new FPVector3(perception.ThreatDirection.X, FP._0, perception.ThreatDirection.Y)
                    : -radial;

                if (BotNavigation.SteerToward(f, ref filter, away, run: false) == true)
                    return;
            }

            // In the band: circle the target, nudged back toward the preferred distance.
            FP radialCorrection = FPMath.Clamp((distance - preferred) / preferred, -FP._1, FP._1) * FP._0_50;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                FPVector3 tangent = new FPVector3(-radial.Z, FP._0, radial.X) * brain->StrafeSign;

                if (BotNavigation.SteerToward(f, ref filter, tangent + radial * radialCorrection, run: false) == true)
                    return;

                // That side runs off a ledge - circle the other way.
                brain->StrafeSign = -brain->StrafeSign;
            }
        }

        private static FP ResolvePreferredFightDistance(Frame f, EntityRef self)
        {
            if (BotSkillUtility.IsHeroSkillActive<JuggernautSkillData>(f, self) == true)
                return FP._1;

            FP range = f.Unsafe.TryGetPointer<Weapon>(self, out var weapon) == true && weapon->WeaponData.IsValid == true
                ? WeaponPerkUtility.ResolveWeaponRange(f, weapon)
                : DefaultWeaponRange;

            return FPMath.Clamp(range * FightRangeFraction, FightMinDistance, FightMaxDistance);
        }

        private static void ExecuteExplore(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings)
        {
            FP arrival = f.Has<Chunk>(filter.Brain->GoalTarget) == true ? ChunkArrivalDistance : ResolvePreferredFightDistance(f, filter.Entity);
            ExecuteMoveTo(f, ref filter, settings, arrival);
        }

        // Walk to the current GoalTarget (a Chunk's centre for a Chunk) through BotNavigation's grid
        // pathfinding.
        private static void ExecuteMoveTo(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, FP arrivalDistance)
        {
            BotBrain* brain = filter.Brain;
            EntityRef target = brain->GoalTarget;
            FPVector3 targetPosition;

            if (f.Has<Chunk>(target) == true)
            {
                if (BotNavigation.TryGetChunkCenter(f, target, out targetPosition) == false)
                    return;
            }
            else if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                targetPosition = targetTransform->Position;
            }
            else
            {
                return;
            }

            FPVector3 position = filter.Transform->Position;
            FP distance = BotNavigation.FlatDistance(position, targetPosition);

            if (distance <= arrivalDistance)
            {
                BotNavigation.ClearPaths(brain);
                return;
            }

            bool run = distance > Or(settings.RunDistance, DefaultRunDistance);

            if (BotNavigation.TryMoveToward(f, ref filter, targetPosition, run) == false)
            {
                IgnoreCurrentTarget(brain, position);
            }
        }

        // Formation slot behind/beside the leader, with stop-and-go hysteresis, and the leash
        // teleport as the last resort when the bot can't get back to them.
        private static void ExecuteFollow(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, EntityRef leader, FPVector3 leaderPosition, FP leaderDistance, bool wasMoving)
        {
            BotBrain* brain = filter.Brain;
            FPVector3 position = filter.Transform->Position;
            FPVector3 slot = ResolveFormationSlot(f, filter.Entity, brain, settings, leader, leaderPosition);
            FP distance = BotNavigation.FlatDistance(position, slot);

            // A bot already walking closes all the way in; a parked one waits for a full slack of
            // drift before setting off again - otherwise it stutters in and out every tick.
            FP resume = wasMoving == true ? SlotArrivalDistance : SlotArrivalDistance + Or(settings.FollowSlack, DefaultFollowSlack);
            bool blocked = false;

            if (distance > resume)
            {
                FP runDistance = Or(settings.RunDistance, DefaultRunDistance);
                bool run = distance > runDistance || leaderDistance > runDistance;
                blocked = BotNavigation.TryMoveToward(f, ref filter, slot, run) == false;
            }
            else
            {
                BotNavigation.ClearPaths(brain);
            }

            UpdateLeash(f, ref filter, settings, leader, leaderPosition, leaderDistance, blocked);
        }

        private static FPVector3 ResolveFormationSlot(Frame f, EntityRef self, BotBrain* brain, RuntimeConfig.BotSettings settings, EntityRef leader, FPVector3 leaderPosition)
        {
            if (brain->FormationRerollTimer <= FP._0 || brain->FormationDistance <= FP._0)
            {
                FP baseAngle = FormationBaseAngles[ResolveBotIndex(f, self) % FormationBaseAngles.Length];
                FP min = Or(settings.FormationOffsetMin, DefaultFormationOffsetMin);
                FP max = Or(settings.FormationOffsetMax, DefaultFormationOffsetMax);
                FP intervalMin = Or(settings.FormationRerollIntervalMin, DefaultFormationRerollIntervalMin);
                FP intervalMax = Or(settings.FormationRerollIntervalMax, DefaultFormationRerollIntervalMax);

                brain->FormationAngle = baseAngle + f.RNG->Next(-FormationJitter, FormationJitter);
                brain->FormationDistance = max > min ? f.RNG->Next(min, max) : min;
                brain->FormationRerollTimer = intervalMax > intervalMin ? f.RNG->Next(intervalMin, intervalMax) : intervalMin;
            }

            FPVector2 heading = brain->LeaderHeading != default ? brain->LeaderHeading : new FPVector2(FP._0, FP._1);

            // The slot, then its mirror on the other side, then the leader's own spot - a slot over
            // water or up a cliff is never a place to walk to.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                FP angle = attempt == 0 ? brain->FormationAngle : 360 - brain->FormationAngle;
                FPVector2 offset = BotNavigation.Rotate(heading, angle) * brain->FormationDistance;
                FPVector3 slot = leaderPosition + new FPVector3(offset.X, FP._0, offset.Y);

                if (BotNavigation.IsStandable(f, slot, leaderPosition.Y, FP._1) == true)
                    return slot;
            }

            return leaderPosition;
        }

        // This bot's position among all bots by PlayerRef - picks its formation side, so two bots
        // following the same human take different sides instead of the same one.
        private static int ResolveBotIndex(Frame f, EntityRef self)
        {
            if (f.Unsafe.TryGetPointer<PlayerLink>(self, out var selfLink) == false)
                return 0;

            int index = 0;
            var bots = f.Filter<PlayerLink, BotBrain>();

            while (bots.Next(out EntityRef entity, out PlayerLink link, out BotBrain _) == true)
            {
                if (entity != self && (int)link.Player < (int)selfLink->Player)
                    index++;
            }

            return index;
        }

        // Stranded = no safe route at all, stuck in place, or simply too far - for LeashTimeout
        // seconds straight. Teleports next to the leader, only onto solid ground (a leader falling
        // into a pit is not a destination).
        private static void UpdateLeash(Frame f, ref Filter filter, RuntimeConfig.BotSettings settings, EntityRef leader, FPVector3 leaderPosition, FP leaderDistance, bool blocked)
        {
            BotBrain* brain = filter.Brain;
            FP leashDistance = Or(settings.LeashDistance, DefaultLeashDistance);

            bool stranded = blocked == true || brain->StuckTimer > FollowStrandedStuckTime || leaderDistance > leashDistance;

            if (stranded == false)
            {
                brain->LeashTimer = FP._0;
                return;
            }

            brain->LeashTimer += f.DeltaTime;

            if (brain->LeashTimer < Or(settings.LeashTimeout, DefaultLeashTimeout))
                return;

            if (f.Unsafe.TryGetPointer<KCC>(leader, out var leaderKcc) == true && leaderKcc->Data.IsGrounded == false)
                return;

            if (BotNavigation.IsStandable(f, leaderPosition, leaderPosition.Y, FP._1) == false)
                return;

            brain->LeashTimer = FP._0;
            brain->StuckTimer = FP._0;
            BotNavigation.Teleport(f, ref filter, leaderPosition);
            brain->ProgressAnchor = leaderPosition;

            Log.Debug($"[Bot] {filter.Entity} was stranded ({(blocked == true ? "no safe route" : leaderDistance > leashDistance ? "too far" : "stuck")}) - teleported to its leader");
        }

        // ---------------------------------------------------------------- stuck handling

        // Wanted to move but went nowhere for StuckTimeout -> give up on the target for a while
        // (Follow instead feeds the leash via StuckTimer).
        private static void UpdateStuck(Frame f, ref Filter filter)
        {
            BotBrain* brain = filter.Brain;
            FPVector3 position = filter.Transform->Position;

            if (brain->Data.Direction == default || filter.KCC->Data.IsGrounded == false)
            {
                brain->ProgressAnchor = position;
                brain->StuckTimer = FP._0;
                return;
            }

            if (BotNavigation.FlatDistance(position, brain->ProgressAnchor) > StuckRadius)
            {
                brain->ProgressAnchor = position;
                brain->StuckTimer = FP._0;
                return;
            }

            brain->StuckTimer += f.DeltaTime;

            if (brain->StuckTimer < StuckTimeout || brain->Goal == BotGoal.Follow)
                return;

            brain->StrafeSign = -brain->StrafeSign;
            IgnoreCurrentTarget(brain, position);
        }

        private static void IgnoreCurrentTarget(BotBrain* brain, FPVector3 position)
        {
            if (brain->GoalTarget != EntityRef.None)
            {
                brain->IgnoredTarget = brain->GoalTarget;
                brain->IgnoreTimer = IgnoreDuration;
            }

            SetGoal(brain, position, BotGoal.None, EntityRef.None);
        }

        // ---------------------------------------------------------------- helpers

        public static FP RetreatFraction(RuntimeConfig.BotSettings settings) => Or(settings.RetreatHealthFraction, DefaultRetreatHealthFraction);

        // Every FP in BotSettings treats 0 as "unauthored".
        public static FP Or(FP value, FP fallback)
        {
            return value > FP._0 ? value : fallback;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform;
            public BotBrain* Brain;

            // Every player avatar is a KCC entity - the grounded check gates the ledge probes, and
            // the leash teleports through it.
            public KCC* KCC;
        }
    }
}
