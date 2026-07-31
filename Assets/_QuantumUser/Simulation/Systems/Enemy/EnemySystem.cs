namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Generic enemy AI shell: Idle -> Chasing -> Preparation -> {Recovery | Active -> Recovery},
    // looping back to Chasing (or Idle if the target is lost/out of leash range). Preparation and
    // Telegraph share one timer/phase pair (see EnemyActionPhase) - the phase flips at
    // EnemyActionData.TelegraphStartPercent through the windup (default halfway; set to 1 to opt
    // an action out entirely). Movement drives PhysicsBody3D.Velocity (PhysicsSystem3D,
    // already in the default SystemsConfig, integrates it) rather than writing Transform3D
    // directly, so Grounded enemies fall off ledges/collide like any other dynamic body, and Flying
    // enemies (GravityScale forced to 0 below) can chase in full 3D.
    //
    // Every enemy has a BasicAction (EnemyDataAsset.BasicAction) and optionally more SkillActions -
    // EnemyDecisionUtility.TrySelectAction picks whichever is eligible/highest-scoring on the
    // Chasing -> Preparation transition, recorded on Enemy.CurrentActionSlot so every later phase
    // handler resolves the same one. Each EnemyActionData in turn points at exactly one
    // EnemyDeliveryData - a self-contained, polymorphic Quantum asset that owns the actual
    // execution logic. This system never knows which concrete delivery type it's driving:
    // EnemyDeliveryData.Begin() is called once when the windup ends and either resolves the action
    // instantly (returns true, e.g. melee/projectile) or needs further ticks (returns false, e.g. a
    // dash) via EnemyDeliveryData.Tick() during EnemyActionPhase.Active. Adding a new delivery type
    // is a new EnemyDeliveryData subclass - zero changes here.
    [Preserve]
    public unsafe class EnemySystem : SystemMainThreadFilter<EnemySystem.Filter>, ISignalOnEnemyDied, ISignalOnEnemyKnockedBack, ISignalOnEntityPrototypeMaterialized
    {
        private const string DeadEnemyLayerName = "DeadEnemy";

        private int? _deadEnemyLayerIndex;

        // Seeds Health/Shield from EnemyDataAsset once the whole prototype is materialized, not
        // from ISignalOnComponentAdded<Enemy> - mirrors CharacterSystem.OnEntityPrototypeMaterialized's
        // own reasoning (components land one at a time, so seeding off Enemy's own add could run
        // before Health/Shield exist yet). Fires for every materialized entity - projectiles,
        // chunks, areas - so the Enemy check below is the filter for "is this an enemy at all".
        public void OnEntityPrototypeMaterialized(Frame f, EntityRef entity, EntityPrototypeRef prototypeRef)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return;

            if (enemy->EnemyData.IsValid == false)
            {
                // Not an error by itself - GroupSpawnerUtility deliberately creates every
                // Director-purchased enemy off one shared generic prototype with no EnemyData
                // baked in, then calls SeedFromEnemyData itself right after setting Enemy->EnemyData
                // (this signal already fired by then, against an empty ref, and did nothing). Only a
                // hand-authored prototype that's supposed to carry its own baked EnemyData and
                // doesn't is a real authoring mistake.
                return;
            }

            SeedFromEnemyData(f, entity, f.FindAsset(enemy->EnemyData));
        }

        // Public so a spawner that creates an entity off a generic prototype (no EnemyData baked
        // in) and assigns Enemy->EnemyData itself afterward - see GroupSpawnerUtility.SpawnMember -
        // can re-run the same Health/Shield/Radius seeding OnEntityPrototypeMaterialized would have
        // done, since that signal already fired (against an empty EnemyData) by the time
        // Enemy->EnemyData is set post-Create.
        public static void SeedFromEnemyData(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            SeedHealth(f, entity, data);
            SeedShield(f, entity, data);
            SeedRadius(f, entity, data);
        }

        // Overrides whatever radius the generic prototype's collider was authored with, so one
        // shared prototype can serve every enemy type/size - see EnemyDataAsset.Stats.Radius.
        // Radius is scaled by EnemyTierStatsConfig.ScaleMultiplier for this enemy's Tier, so
        // tougher tiers read as visibly bigger by default without hand-authoring size per enemy -
        // this is the single seed point the view reads back from (EnemyMovementUtility.
        // ResolveEntityRadius), so the sprite fit picks up the tier scale automatically too.
        private static void SeedRadius(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
            {
                Log.Error($"[Enemy] {entity} ({data.name}) has no PhysicsCollider3D - Radius {data.Stats.Radius} was not applied");
                return;
            }

            FP radius = data.Stats.Radius * EnemyTierStatsConfig.Resolve(f, data.Tier).ScaleMultiplier;
            collider->Shape = Shape3D.CreateSphere(radius);
            Log.Debug($"[Enemy] {entity} ({data.name}) collider radius seeded to {radius}");
        }

        // Health may legitimately be absent (an enemy that can't be damaged the normal way, e.g. a
        // pure trigger/hazard entity also using the Enemy component), so this is not an error case.
        private static void SeedHealth(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            health->MaxHealth = EnemyTierStatsConfig.Resolve(f, data.Tier).MaxHealth;
            health->CurrentHealth = health->MaxHealth;
        }

        // Unlike the player's own Shield (CharacterSystem.SeedShield), which only seeds an
        // already-authored Shield component - a hero either has one on their prefab or doesn't -
        // this drives it purely from data: MaxShield > 0 dynamically adds the Shield component
        // instead of requiring every shielded enemy variant to remember to author one on its own
        // prefab. An enemy whose prefab already happens to have one (e.g. hand-authored for tuning)
        // is reseeded in place rather than double-added.
        private static void SeedShield(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (data.Stats.MaxShield <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == false)
            {
                if (f.Add(entity, out shield) != AddResult.ComponentAdded)
                    return;
            }

            shield->Max = data.Stats.MaxShield;
            shield->Current = shield->Max;
            shield->RechargeDelay = data.Stats.ShieldRechargeDelay;
            shield->RechargeRate = data.Stats.ShieldRechargeRate;
            shield->RechargeTimer = FP._0;

            if (data.Stats.ShieldRechargeRate <= FP._0)
                Log.Error($"[Enemy] {entity} has a shield but {data.name} authors ShieldRechargeRate 0 - it will never recharge");
        }

        public override void Update(Frame f, ref Filter filter)
        {
            // Dead is a terminal state (set by DamageUtility, not looped back into from here) -
            // skip the rest of the AI entirely so a corpse doesn't keep chasing/attacking during
            // its DeathLingerTime countdown. GravityScale is still applied below so a dead flying
            // enemy still drops to the ground instead of hanging in the air.
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
            {
                UpdateDead(f, ref filter);
                return;
            }

            if (CheckFallDeath(f, ref filter) == true)
                return;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);

            filter.PhysicsBody3D->GravityScale = data.Stats.Height.InitialState == EnemyHeightState.Flying ? FP._0 : FP._1;

            TickAttackCooldown(f, ref filter);

            // Root takes priority over TickKnockbackRecovery's own "still airborne" gate below - a
            // Rooted enemy is meant to freeze wherever it currently is (mid-air against a wall
            // included), not keep waiting to touch real ground first the way a plain knockback does.
            // Going kinematic (not just zeroing Velocity) is what makes it "stuck" rather than just
            // "stopped": a kinematic body is skipped by PhysicsSystem3D entirely, so gravity and
            // knockback/collision impulses can't drag it down or off a wall either - exactly the
            // "stuck on the wall" look Landing Root wants. The state machine below still runs
            // (Stun aside) so an already-in-range enemy keeps attacking; only movement is pinned.
            bool isRooted = StatusEffectUtility.IsRooted(f, filter.Entity);

            if (isRooted == true)
            {
                filter.PhysicsBody3D->IsKinematic = true;
                EnemyMovementUtility.StopMovement(f, ref filter, data);

                // Stun still fully freezes (state machine included) even while also Rooted - Root
                // alone leaves attacking untouched, but Stun's own total lockdown takes precedence.
                if (StatusEffectUtility.IsStunned(f, filter.Entity) == true)
                    return;
            }
            else
            {
                // Restored here BEFORE TickKnockbackRecovery's own check below, not after - a body
                // left kinematic never falls under gravity, so it can never become newly grounded;
                // gating this reset behind TickKnockbackRecovery passing would deadlock forever the
                // instant Root ends while the enemy isn't already resting on real ground (exactly the
                // wall-stuck case Landing Root creates), since "still airborne" would keep returning
                // true every tick, which would keep skipping the one line that lets it start falling
                // again. Skipped during Active so this doesn't clobber a Charge attack's own
                // kinematic flag (ChargeDeliveryData.Begin sets it, EnterRecovering already restores it
                // respecting Root when the attack finishes) - and skipped while a
                // JuggernautExplosionPush is in progress for the exact same reason (that system also
                // drives the enemy kinematically for a short duration and restores this itself when done).
                if (filter.Enemy->Phase != EnemyActionPhase.Active && f.Has<JuggernautExplosionPush>(filter.Entity) == false)
                {
                    filter.PhysicsBody3D->IsKinematic = false;
                }

                if (TickKnockbackRecovery(f, ref filter, data) == true)
                    return;

                // Unlike knockback recovery, there's no impulse to preserve here, so Stun stops the
                // enemy outright instead of just leaving velocity alone - see StatusEffectUtility.
                // Decrementing StunRemaining is StatusEffectSystem's job, not this system's; this
                // only reads it.
                if (StatusEffectUtility.IsStunned(f, filter.Entity) == true)
                {
                    EnemyMovementUtility.StopMovement(f, ref filter, data);
                    return;
                }
            }

            switch (filter.Enemy->Phase)
            {
                case EnemyActionPhase.Idle:
                    UpdateIdle(f, ref filter, data);
                    break;

                case EnemyActionPhase.Chasing:
                    UpdateChasing(f, ref filter, data);
                    break;

                case EnemyActionPhase.Preparation:
                case EnemyActionPhase.Telegraph:
                    UpdatePreparation(f, ref filter, data);
                    break;

                case EnemyActionPhase.Active:
                    UpdateActive(f, ref filter, data);
                    break;

                case EnemyActionPhase.Recovery:
                    UpdateRecovery(f, ref filter, data);
                    break;
            }
        }

        private static void UpdateDead(Frame f, ref Filter filter)
        {
            filter.PhysicsBody3D->GravityScale = FP._1;

            // Re-zeroed every tick, not just once in OnEnemyDied - collision resolution against
            // environment geometry (e.g. settling on a slope) can otherwise keep nudging the
            // corpse in X/Z for the rest of its lingering duration. Y is left alone so it still
            // falls/settles.
            filter.PhysicsBody3D->Velocity = new FPVector3(FP._0, filter.PhysicsBody3D->Velocity.Y, FP._0);

            filter.Enemy->StateTimer -= f.DeltaTime;

            if (filter.Enemy->StateTimer <= FP._0)
            {
                f.Destroy(filter.Entity);
            }
        }

        // Stops the corpse dead in its tracks (Y velocity from the death blow is kept so it still
        // falls/settles) and moves it onto the DeadEnemy physics layer, which the collision matrix
        // (SimulationConfig) restricts to environment layers only - so players/enemies can no
        // longer shove a corpse around, but it still rests on the ground and behind walls.
        public void OnEnemyDied(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(entity, out var body) == true)
            {
                body->Velocity = new FPVector3(FP._0, body->Velocity.Y, FP._0);
            }

            _deadEnemyLayerIndex ??= f.Layers.GetLayerIndex(DeadEnemyLayerName);

            if (_deadEnemyLayerIndex.Value >= 0 && f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Layer = (byte)_deadEnemyLayerIndex.Value;
            }
        }

        // A large multiple of MaxHealth rather than a fixed huge constant - guarantees the hit
        // still blows through any Armor/Shield mitigation (both finite) regardless of the enemy's
        // own health scale, same DamageUtility pipeline as every other death.
        private static bool CheckFallDeath(Frame f, ref Filter filter)
        {
            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (filter.Transform3D->Position.Y >= config.FallDeathHeight)
                return false;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == false)
                return false;

            Log.Debug($"[Enemy] {filter.Entity} fell below FallDeathHeight={config.FallDeathHeight} " +
                      $"(Y={filter.Transform3D->Position.Y}) - killed by the fall");

            DamageUtility.ApplyDamage(f, filter.Entity, health->MaxHealth * 1000, EntityRef.None, bypassOutgoingResolution: true);
            return true;
        }

        private static void TickAttackCooldown(Frame f, ref Filter filter)
        {
            if (filter.Enemy->AttackCooldownRemaining > FP._0)
            {
                filter.Enemy->AttackCooldownRemaining -= f.DeltaTime;
            }

            // Only present on enemies with SkillActions configured - see EnemyActionSlots.
            if (f.Unsafe.TryGetPointer<EnemyActionSlots>(filter.Entity, out var slots) == true)
            {
                for (int i = 0; i < slots->SkillCooldowns.Length; i++)
                {
                    if (slots->SkillCooldowns[i] > FP._0)
                    {
                        slots->SkillCooldowns[i] -= f.DeltaTime;
                    }
                }
            }
        }

        // Parks the whole state machine while the stagger window is open, which is the entire point:
        // every state below writes PhysicsBody3D.Velocity every tick (StopMovement zeroes X/Z,
        // MoveInDirection overwrites them), so letting any of them run would erase a knockback
        // impulse before it carried the enemy anywhere. Nothing here writes velocity - physics
        // integrates the push and Drag settles it. Returns true while the enemy is still staggered.
        //
        // KnockbackRecoveryTime is only an authored guess at how long the pop takes to settle -
        // physics can legitimately take longer (a harder pop, stacked impulses, a slope) than that
        // fixed window assumes. Once it elapses, a Grounded enemy that's still visibly airborne keeps
        // the freeze going instead of resuming ground-chase movement while floating; a Flying enemy
        // has nothing to check against, so it's exempt. This can't softlock an enemy that's fallen
        // into a genuine void - CheckFallDeath (called earlier in Update) kills it once it crosses
        // LevelConfig.FallDeathHeight regardless of state.
        private static bool TickKnockbackRecovery(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            if (filter.Enemy->KnockbackTimer > FP._0)
            {
                filter.Enemy->KnockbackTimer -= f.DeltaTime;
                return true;
            }

            // Skipped during Active - this "wait until grounded again" gate exists for a REAL
            // knockback launch (PhysicsBody3D.Velocity-driven, non-kinematic - gravity has to carry
            // it back down before the AI resumes). A kinematic delivery like Leap is airborne on
            // purpose for the whole span of its own Tick(), which never touches the ground again
            // until Tick() itself snaps it down on landing - without this guard, the instant Leap
            // lifts off, IsGrounded goes false and this starts returning true every tick, which
            // makes the caller bail out before ever reaching UpdateActive/delivery.Tick() again -
            // the leap freezes mid-air forever, StateTimer never decrementing.
            if (filter.Enemy->Phase != EnemyActionPhase.Active &&
                data.Stats.Height.InitialState == EnemyHeightState.Grounded &&
                EnemyMovementUtility.IsGrounded(f, filter.Entity, filter.Transform3D->Position, EnemyMovementUtility.GetGroundLayerMask(f)) == false)
            {
                return true;
            }

            return false;
        }

        // Opens the stagger window and, for an enemy caught mid-action, checks whether this
        // specific action's InterruptibleDuringTelegraph/InterruptibleDuringActive says a knockback
        // should throw away what it was doing. Active still reaches this check today only in
        // theory - today's kinematic deliveries (Charge/Leap) never receive a real impulse while
        // Active in the first place (DamageUtility.ApplyResolvedImpulse skips a kinematic
        // PhysicsBody3D, so this signal never even fires for them then), so InterruptibleDuringActive
        // only matters once a non-kinematic multi-tick delivery exists.
        public void OnEnemyKnockedBack(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return;

            if (enemy->Phase == EnemyActionPhase.Dead)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            if (data.Knockback.CanBeInterruptedByKnockback == false)
            {
                Log.Debug($"[Knockback] {entity} is not interruptible - AI keeps driving velocity, so the push dies on its next tick");
                return;
            }

            enemy->KnockbackTimer = data.Knockback.KnockbackRecoveryTime;

            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, enemy->CurrentActionSlot);

            if (action != null)
            {
                if ((enemy->Phase == EnemyActionPhase.Preparation || enemy->Phase == EnemyActionPhase.Telegraph)
                    && action.InterruptibleDuringTelegraph == true)
                {
                    CancelWindup(f, entity, enemy, action);
                }
                else if (enemy->Phase == EnemyActionPhase.Active && action.InterruptibleDuringActive == true)
                {
                    CancelActive(f, entity, enemy, data, action);
                }
            }

            Log.Debug($"[Knockback] {entity} staggered for {data.Knockback.KnockbackRecoveryTime}s, Phase {enemy->Phase}");
        }

        // Drops a cancelled windup straight into Recovery, paying this action's full cooldown so a
        // staggered enemy can't immediately re-wind the action it just lost. Deliberately not
        // EnterRecovering - that one calls StopMovement, which would zero the very impulse the
        // stagger exists to preserve. Begin() never ran, so there's no delivery-side cleanup to call.
        private static void CancelWindup(Frame f, EntityRef entity, Enemy* enemy, EnemyActionData action)
        {
            EnemyDecisionUtility.SetCooldownRemaining(f, entity, enemy, enemy->CurrentActionSlot, action.CooldownTime);
            enemy->StateTimer = action.DownTime;
            enemy->Phase = EnemyActionPhase.Recovery;
        }

        // Active-phase counterpart to CancelWindup - resolves the pointers this signal handler
        // doesn't already have (only Enemy* comes for free here, unlike Update's full Filter) so
        // the interrupted delivery gets a real OnInterrupted call before losing the action.
        private static void CancelActive(Frame f, EntityRef entity, Enemy* enemy, EnemyDataAsset data, EnemyActionData action)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(entity, out var body) == false)
                return;

            if (f.Unsafe.TryGetPointer<Aim>(entity, out var aim) == false)
                return;

            Filter filter = new Filter
            {
                Entity = entity,
                Enemy = enemy,
                Transform3D = transform,
                PhysicsBody3D = body,
                Aim = aim,
            };

            EnemyDeliveryData delivery = f.FindAsset(action.Delivery);
            delivery.OnInterrupted(f, ref filter, data, action);

            EnemyDecisionUtility.SetCooldownRemaining(f, entity, enemy, enemy->CurrentActionSlot, action.CooldownTime);
            enemy->StateTimer = action.DownTime;
            enemy->Phase = EnemyActionPhase.Recovery;
        }

        private static void UpdateIdle(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            EnemyMovementUtility.StopMovement(f, ref filter, data);

            EntityRef target = ResolveInitialTarget(f, ref filter, data);

            if (target != EntityRef.None)
            {
                filter.Enemy->Target = target;
                filter.Enemy->Phase = EnemyActionPhase.Chasing;
                Log.Debug($"[Enemy] {filter.Entity} detected {target} within DetectionRange={data.AI.DetectionRange}, switching Idle -> Chasing");
            }
        }

        // Decoy priority ("max aggro") applies no matter which EnemyTargetingData is configured -
        // it's an override on top of the chosen policy, not baked into any one profile (see
        // EnemyTargetingData's own class comment). Reproduces this system's original (pre-modular)
        // target-acquisition behavior exactly when Targeting is a NearestPlayerTargetingData: decoy
        // first, else whatever the profile picks.
        private static EntityRef ResolveInitialTarget(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            if (EnemyMovementUtility.TryFindNearestDecoy(f, filter.Transform3D->Position, data.AI.DetectionRange, out EntityRef decoyTarget) == true)
                return decoyTarget;

            if (data.AI.Targeting.IsValid == false)
                return EntityRef.None;

            return f.FindAsset(data.AI.Targeting).SelectTarget(f, filter.Entity);
        }

        private static void UpdateChasing(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, filter.Enemy->Target, out FPVector3 targetPosition) == false)
            {
                Log.Debug($"[Enemy] {filter.Entity} lost target {filter.Enemy->Target} (no longer exists), switching Chasing -> Idle");
                filter.Enemy->Target = EntityRef.None;
                filter.Enemy->Phase = EnemyActionPhase.Idle;
                return;
            }

            FPVector3 selfPosition = filter.Transform3D->Position;

            // "Max aggro": a decoy pulls even an already-chasing enemy off its current (possibly
            // real player) target, not just enemies still Idle - see
            // EnemyMovementUtility.TryFindNearestDecoy. Preparation/Telegraph/Active are
            // deliberately not covered - an enemy already committed to a windup/attack doesn't
            // retarget mid-attack.
            if (EnemyMovementUtility.TryFindNearestDecoy(f, selfPosition, data.AI.DetectionRange, out EntityRef decoyTarget) == true && decoyTarget != filter.Enemy->Target)
            {
                Log.Debug($"[Enemy] {filter.Entity} retargeting {filter.Enemy->Target} -> decoy {decoyTarget} (max aggro)");
                filter.Enemy->Target = decoyTarget;
                EnemyMovementUtility.TryGetTargetPosition(f, decoyTarget, out targetPosition);
            }

            FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(selfPosition, targetPosition);

            if (sqrDistance > data.AI.LeashRange * data.AI.LeashRange)
            {
                Log.Debug($"[Enemy] {filter.Entity} target {filter.Enemy->Target} exceeded LeashRange={data.AI.LeashRange} (sqrDistance={sqrDistance}), switching Chasing -> Idle");
                filter.Enemy->Target = EntityRef.None;
                filter.Enemy->Phase = EnemyActionPhase.Idle;
                return;
            }

            if (EnemyDecisionUtility.TrySelectAction(f, filter.Entity, filter.Enemy, data, targetPosition, sqrDistance, out EnemyActionData action, out int slot) == true)
            {
                filter.Enemy->CurrentActionSlot = (byte)slot;
                filter.Enemy->StateTimer = action.AnticipationTime;
                filter.Enemy->Phase = EnemyActionPhase.Preparation;
                EnemyMovementUtility.StopMovement(f, ref filter, data);
                EnemyMovementUtility.FaceTarget(filter.Aim, selfPosition, targetPosition);

                // First-draft "plan" - a fresh capture the instant the enemy commits to this
                // action, so anything reading these during Preparation/Telegraph (e.g. a ground
                // telegraph spanning the whole windup) sees the real intended target/direction from
                // tick one instead of a stale value left over from a previous action cycle (these
                // fields previously only got set once inside each delivery's own Begin(), which
                // doesn't run until the windup ends). Deliveries that need a more precise point than
                // "the target's raw position right now" (e.g. Charge's DashDistance-clamped
                // endpoint) still overwrite this themselves in their own Begin().
                filter.Enemy->SkillStartPosition = selfPosition;
                filter.Enemy->SkillTargetPosition = targetPosition;
                return;
            }

            // Unlike Stun (which short-circuits the whole state machine in Update, above), Root only
            // pins this one case - actually walking toward the target. The inRange branch above
            // (attacking) and every other state already either don't move or zero their own
            // movement, so a Rooted enemy already in range keeps attacking normally; it just can't
            // close distance to get there.
            if (StatusEffectUtility.IsRooted(f, filter.Entity) == true)
            {
                EnemyMovementUtility.StopMovement(f, ref filter, data);
                return;
            }

            FP moveSpeed = data.Stats.MoveSpeed * StatusEffectUtility.GetSpeedMultiplier(f, filter.Entity);

            FPVector2 direction = data.Stats.Movement.IsValid == true
                ? f.FindAsset(data.Stats.Movement).ComputeMoveDirection(f, filter.Entity, filter.Enemy->Target)
                : default;

            EnemyMovementUtility.MoveInDirection(f, ref filter, data, direction, moveSpeed);
        }

        // Drives both Preparation and Telegraph off the same single windup timer - Telegraph is
        // just "how much of the windup has elapsed" crossing TelegraphStartPercent, not a second
        // timer. An action authored with TelegraphStartPercent=1 never flips (elapsed only reaches
        // 1 the same tick StateTimer hits 0, right as Begin() is about to be called) - a valid way
        // to opt an action out of a visible Telegraph phase entirely.
        private static void UpdatePreparation(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            // Re-zeroed every tick, not just once on entering this phase - collision resolution
            // (e.g. bumping into the target) can otherwise nudge the enemy out of place during the
            // windup instead of it staying fully planted for the telegraph.
            EnemyMovementUtility.StopMovement(f, ref filter, data);

            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, filter.Enemy->CurrentActionSlot);
            EnemyDeliveryData delivery = f.FindAsset(action.Delivery);
            delivery.OnAnticipating(f, ref filter, data, action, filter.Enemy->Target);

            FP anticipationMultiplier = StatusEffectUtility.GetAnticipationMultiplier(f, filter.Entity);
            filter.Enemy->StateTimer -= f.DeltaTime * anticipationMultiplier;

            if (filter.Enemy->StateTimer > FP._0)
            {
                if (filter.Enemy->Phase == EnemyActionPhase.Preparation)
                {
                    FP elapsed = action.AnticipationTime > FP._0
                        ? FP._1 - filter.Enemy->StateTimer / action.AnticipationTime
                        : FP._1;

                    if (elapsed >= action.TelegraphStartPercent)
                    {
                        filter.Enemy->Phase = EnemyActionPhase.Telegraph;
                    }
                }

                return;
            }

            bool finished = delivery.Begin(f, ref filter, data, action, filter.Enemy->Target);

            if (finished == true)
            {
                EnterRecovering(f, ref filter, data, action);
            }
            else
            {
                filter.Enemy->Phase = EnemyActionPhase.Active;
            }
        }

        private static void UpdateActive(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, filter.Enemy->CurrentActionSlot);
            EnemyDeliveryData delivery = f.FindAsset(action.Delivery);

            // Refreshed here before Tick() runs - any delivery that reads Enemy.SkillTargetPosition
            // each tick (e.g. ChargeDeliveryData.Tick moving toward it) automatically follows the
            // live target when DirectionTracking is UpdateTargetDirectionWhileActive, with no
            // changes needed in the delivery itself.
            if (action.DirectionTracking == DirectionUpdateMode.UpdateTargetDirectionWhileActive &&
                EnemyMovementUtility.TryGetTargetPosition(f, filter.Enemy->Target, out FPVector3 targetPosition) == true)
            {
                filter.Enemy->SkillTargetPosition = targetPosition;
            }

            bool finished = delivery.Tick(f, ref filter, data, action, filter.Enemy->Target);

            if (finished == true)
            {
                EnterRecovering(f, ref filter, data, action);
            }
        }

        private static void UpdateRecovery(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            filter.Enemy->StateTimer -= f.DeltaTime;

            if (filter.Enemy->StateTimer > FP._0)
                return;

            if (EnemyMovementUtility.TryGetTargetPosition(f, filter.Enemy->Target, out FPVector3 targetPosition) == false)
            {
                filter.Enemy->Target = EntityRef.None;
                filter.Enemy->Phase = EnemyActionPhase.Idle;
                return;
            }

            FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(filter.Transform3D->Position, targetPosition);
            bool inLeashRange = sqrDistance <= data.AI.LeashRange * data.AI.LeashRange;

            filter.Enemy->Phase = inLeashRange ? EnemyActionPhase.Chasing : EnemyActionPhase.Idle;

            if (inLeashRange == false)
            {
                filter.Enemy->Target = EntityRef.None;
            }
        }

        // Single call site for every way an attack can finish (instant Begin(), or Tick()
        // reporting done) regardless of concrete type - restores normal (non-kinematic) physics
        // in case the attack moved the enemy kinematically (e.g. Charge), stops movement (an attack
        // can still be moving at speed the instant it finishes), starts this attack's reuse
        // cooldown, and hands the enemy off to its stationary recovery beat. IsKinematic is
        // restored to whatever Root currently wants, not unconditionally false - an attack finishing
        // while still Rooted should stay pinned instead of this un-freezing it early.
        private static void EnterRecovering(Frame f, ref Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            // A delivery can kill its own enemy while resolving (e.g. GroundAreaDeliveryData.
            // SelfDestructs, a creeper-style suicide exploder) - DamageUtility.ApplyDamage already
            // set Phase = Dead and StateTimer = DeathLingerTime by the time Begin()/Tick() returns
            // "finished" here, and death always takes priority over a normal Recovery transition -
            // overwriting either would clobber the real death pipeline's own bookkeeping and leave
            // the corpse stuck (UpdateDead never runs, so it never actually gets destroyed).
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            filter.PhysicsBody3D->IsKinematic = StatusEffectUtility.IsRooted(f, filter.Entity);
            EnemyMovementUtility.StopMovement(f, ref filter, data);
            EnemyDecisionUtility.SetCooldownRemaining(f, filter.Entity, filter.Enemy, filter.Enemy->CurrentActionSlot, action.CooldownTime);
            filter.Enemy->StateTimer = action.DownTime;
            filter.Enemy->Phase = EnemyActionPhase.Recovery;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public Transform3D* Transform3D;
            public PhysicsBody3D* PhysicsBody3D;
            public Aim* Aim;
        }
    }
}
