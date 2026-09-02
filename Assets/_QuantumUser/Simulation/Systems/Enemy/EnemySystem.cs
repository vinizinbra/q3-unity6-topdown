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
        // How long every enemy sits inert right after spawning before its AI state machine starts
        // running at all - see Enemy.SpawnGraceRemaining's own comment and the gate in Update below.
        // A flat global value, not per-EnemyDataAsset - this is about masking spawn-frame jank (an
        // enemy popping in mid-air, or instantly locking onto a player the same tick it materializes),
        // not a per-enemy-type balance knob.
        private static readonly FP SpawnGraceDuration = FP._0_10 * 3;

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
            if (data == null)
            {
                Log.Error($"[Enemy] {entity} EnemyData did not resolve to an asset (dangling/unassigned AssetRef) - skipping Health/Shield/Radius/WaypointPath seeding");
                return;
            }

            EnemyRuntimeStats stats = EnemyBalanceUtility.ResolveEnemyStats(f, data.Tier);

            SeedHealth(f, entity, data, stats);
            SeedCombatModifiers(f, entity, stats);
            SeedShield(f, entity, data);
            SeedRadius(f, entity, data);
            SeedWaypointPath(f, entity, data);
            SeedChargeHitTracking(f, entity, data);
            SeedSpawnGrace(f, entity);
        }

        // See SpawnGraceDuration's own comment. Unconditional - every enemy carries the shared
        // Enemy component, so unlike e.g. SeedShield there's no "does this one even have the
        // relevant component" gate to check first.
        private static void SeedSpawnGrace(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return;

            enemy->SpawnGraceRemaining = SpawnGraceDuration;
        }

        // UseWaypointDetour (EnemyPathfindingUtility.TryGetDetourDirection) needs somewhere to
        // cache its resolved path between ticks - added dynamically here, the same way SeedShield
        // adds Shield only when this enemy actually has one, rather than requiring every prototype
        // that flips the checkbox to also remember to hand-author EnemyWaypointPath.
        private static void SeedWaypointPath(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (data.Stats.UseWaypointDetour == false)
                return;

            if (f.Unsafe.TryGetPointer<EnemyWaypointPath>(entity, out _) == true)
                return;

            f.Add<EnemyWaypointPath>(entity);
        }

        // ChargeDeliveryData's own per-target hit cooldown (see its HitCooldown field) needs
        // somewhere to store per-target state - added dynamically, only for an enemy whose
        // BasicAction/SkillActions actually resolves to a ChargeDeliveryData with StopOnHit =
        // false, same "only add what THIS enemy needs, driven by data" shape SeedShield/
        // SeedWaypointPath already use above, rather than requiring ChargeHitTracking to be
        // hand-authored onto the one shared generic prototype (which would make every enemy in
        // the game - including ones with no charge at all - carry tracking data only a
        // StopOnHit=false charger ever reads).
        private static void SeedChargeHitTracking(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            if (f.Unsafe.TryGetPointer<ChargeHitTracking>(entity, out _) == true)
                return;

            if (NeedsChargeHitTracking(f, data) == false)
                return;

            f.Add<ChargeHitTracking>(entity);
        }

        private static bool NeedsChargeHitTracking(Frame f, EnemyDataAsset data)
        {
            if (ActionNeedsChargeHitTracking(f, data.Actions.BasicAction) == true)
                return true;

            for (int i = 0; i < data.Actions.SkillActions.Count; i++)
            {
                if (ActionNeedsChargeHitTracking(f, data.Actions.SkillActions[i]) == true)
                    return true;
            }

            return false;
        }

        private static bool ActionNeedsChargeHitTracking(Frame f, AssetRef<EnemyActionData> actionRef)
        {
            if (actionRef.IsValid == false)
                return false;

            EnemyActionData action = f.FindAsset(actionRef);

            if (action == null || action.Delivery.IsValid == false)
                return false;

            EnemyDeliveryData delivery = f.FindAsset(action.Delivery);
            return delivery is ChargeDeliveryData chargeDelivery && chargeDelivery.StopOnHit == false;
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
        // MaxHealth comes from the once-per-spawn EnemyBalanceUtility.ResolveEnemyStats snapshot -
        // EnemyTierStatsConfig.MaxHealth scaled by BalanceConfig's run curves/co-op multipliers,
        // not read directly here - see docs/run-curves-coop-scaling.md - further scaled by this
        // enemy's own EnemyDataAsset.Stats.HealthMultiplier, an author-facing per-enemy correction
        // on top of that shared curve (1 by default, same role ShieldMultiplier plays for Shield).
        private static void SeedHealth(Frame f, EntityRef entity, EnemyDataAsset data, EnemyRuntimeStats stats)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            // Run-wide encounter modifiers (Greed's +50%, Overpopulation's -25%; see
            // RunMutations.qtn) - exactly 1x for a run where none was picked. Tier-aware: a
            // NEGATIVE total is ignored for a Boss, so a horde mutation can't trivialise a boss
            // fight. Baked once here, so already-alive enemies keep whatever they rolled.
            FP healthMultiplier = EncounterModifierUtility.ResolveEnemyHealthMultiplier(f, data.Tier);

            health->MaxHealth = stats.MaxHp * healthMultiplier * data.Stats.HealthMultiplier;
            health->CurrentHealth = health->MaxHealth;
        }

        // EnemyCombatModifiers is hand-authored (not dynamically added, unlike Shield) - absent
        // until every enemy prototype gets it added in the Editor; see the .qtn file's own comment.
        private static void SeedCombatModifiers(Frame f, EntityRef entity, EnemyRuntimeStats stats)
        {
            if (f.Unsafe.TryGetPointer<EnemyCombatModifiers>(entity, out var modifiers) == false)
                return;

            modifiers->DamageMultiplier = stats.DamageMultiplier;
        }

        // Unlike the player's own Shield (CharacterSystem.SeedShield), which only seeds an
        // already-authored Shield component - a hero either has one on their prefab or doesn't -
        // this drives it purely from data: this tier's EnemyTierStatsConfig.Shield baseline times
        // Stats.ShieldMultiplier, and only if that's > 0, dynamically adds the Shield component
        // instead of requiring every shielded enemy variant to remember to author one on its own
        // prefab. An enemy whose prefab already happens to have one (e.g. hand-authored for tuning)
        // is reseeded in place rather than double-added.
        private static void SeedShield(Frame f, EntityRef entity, EnemyDataAsset data)
        {
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);
            FP maxShield = data.Stats.ShieldMultiplier * tierStats.Shield;
            if (maxShield <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == false)
            {
                if (f.Add(entity, out shield) != AddResult.ComponentAdded)
                    return;
            }

            shield->Max = maxShield;
            shield->Current = shield->Max;
            shield->RechargeDelay = tierStats.ShieldRechargeDelay;
            shield->RechargeRate = tierStats.ShieldRechargeRate;
            shield->RechargeTimer = FP._0;

            if (tierStats.ShieldRechargeRate <= FP._0)
                Log.Error($"[Enemy] {entity} has a shield but tier {data.Tier} authors ShieldRechargeRate 0 - it will never recharge");
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

            // Before ANY movement work below (including TickKnockbackRecovery's own early-out, since
            // a push can bury an enemy mid-stagger): if a knockback drove this enemy inside level
            // geometry, put it back where it was standing when it got hit. No-ops entirely unless
            // this specific enemy was knocked back in the last few seconds - see
            // EnemyStuckRecoveryUtility for why this is a recovery rather than a clamp on knockback.
            if (EnemyStuckRecoveryUtility.Tick(f, filter.Entity, filter.Enemy, filter.Transform3D, filter.PhysicsBody3D) == true)
                return;

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
                // Same reasoning for a traversal hop (EnemyMovementUtility.BeginTraversalJump) - it
                // runs during plain Chasing (never Active), so without this exemption this line
                // clobbered the hop's own kinematic flag back to false on the very next tick, every
                // tick, fighting TickTraversalJump's own position writes for the hop's whole arc.
                // TickTraversalJump restores this itself on landing, same as the other two.
                // Also exempted: Preparation/Telegraph - UpdatePreparation drives its own kinematic
                // flag (planted during windup, released right before Begin() runs) so a telegraphing
                // enemy can't be shoved off its readable attack spot by another enemy's collision;
                // without this exemption this line would stomp that back to false every tick before
                // UpdatePreparation even runs.
                if (filter.Enemy->Phase != EnemyActionPhase.Active &&
                    filter.Enemy->Phase != EnemyActionPhase.Preparation &&
                    filter.Enemy->Phase != EnemyActionPhase.Telegraph &&
                    f.Has<JuggernautExplosionPush>(filter.Entity) == false &&
                    filter.Enemy->TraversalJumpDuration <= FP._0)
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

            // Spawn grace (see SpawnGraceDuration's own comment) - deliberately placed after every
            // physics/status handler above (gravity, stuck recovery, knockback, Root, Stun) so a
            // freshly-spawned enemy still settles onto the ground and reacts to being hit normally;
            // only the AI state machine below (targeting/movement/attacking) is held off.
            if (filter.Enemy->SpawnGraceRemaining > FP._0)
            {
                filter.Enemy->SpawnGraceRemaining -= f.DeltaTime;
                return;
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
            // The collider is disabled outright the instant the enemy dies (see OnEnemyDied), so the
            // corpse can no longer rest on ground geometry - kill gravity and freeze it completely so
            // it plays its death animation exactly where it fell instead of dropping through the world.
            filter.PhysicsBody3D->GravityScale = FP._0;
            filter.PhysicsBody3D->Velocity = FPVector3.Zero;

            filter.Enemy->StateTimer -= f.DeltaTime;

            if (filter.Enemy->StateTimer <= FP._0)
            {
                f.Destroy(filter.Entity);
            }
        }

        // Disables the corpse's collider outright the instant it dies so nothing - players, enemies,
        // projectiles, or environment - collides with the lingering body during its DeathLingerTime.
        // Velocity is zeroed here too, and UpdateDead then freezes it in place every tick (with no
        // collider there's nothing left to rest on, so it can't be allowed to keep falling).
        public void OnEnemyDied(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(entity, out var body) == true)
            {
                body->Velocity = FPVector3.Zero;
            }

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Enabled = false;
            }
        }

        // A large multiple of MaxHealth rather than a fixed huge constant - guarantees the hit
        // still blows through any Armor/Shield mitigation (both finite) regardless of the enemy's
        // own health scale, same DamageUtility pipeline as every other death.
        //
        // Boss/Elite/Persistent are deliberately excluded - EnemyFallSystem owns their fall
        // handling instead (respawn to safety, never actually die - confirmed with the user), same
        // split that system's own header comment already documents. Without this exclusion, THIS
        // check killed them outright before EnemyFallSystem (registered right after EnemySystem, so
        // it runs later this same tick) ever got a chance to run its own respawn-safe logic.
        private static bool CheckFallDeath(Frame f, ref Filter filter)
        {
            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (filter.Transform3D->Position.Y >= config.FallDeathHeight)
                return false;

            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);
            if (data.Tier == EnemyTier.Boss || data.Tier == EnemyTier.Elite || data.Economy.Persistent == true)
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
            // the leap freezes mid-air forever, StateTimer never decrementing. Same reasoning, same
            // fix, for a traversal hop (EnemyMovementUtility.BeginTraversalJump) - it runs during
            // Chasing (never Active) and is deliberately airborne mid-arc, so without this second
            // exemption this gate started returning true the instant the hop left the ground,
            // permanently skipping UpdateChasing/TickTraversalJump before it ever got a chance to
            // land - the exact "stuck the moment it has no ground" freeze this comment already
            // describes for Leap, just hitting a feature that runs outside Active instead.
            //
            // Also skipped during Preparation/Telegraph - unlike Chasing, neither phase drives
            // movement (UpdatePreparation only counts StateTimer down and checks the phase
            // transition; StopMovement already zeroed velocity when the windup began), so there's
            // no AI-vs-gravity fight to protect against here. Without this exemption, an incidental
            // knockback impulse landing mid-windup (or just a bit of physics jitter over uneven
            // ground) that briefly lifts the enemy off IsGrounded would freeze StateTimer entirely -
            // the attack silently stops charging until the enemy resettles, even though it never
            // actually left its spot.
            if (filter.Enemy->Phase != EnemyActionPhase.Active &&
                filter.Enemy->Phase != EnemyActionPhase.Preparation &&
                filter.Enemy->Phase != EnemyActionPhase.Telegraph &&
                filter.Enemy->TraversalJumpDuration <= FP._0 &&
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
        //
        // canInterrupt is the pushing source's own declaration (KnockbackEffectData.CanInterrupt,
        // default true) that this specific push is allowed to reach the cancel branch at all - false
        // for a cosmetic/juice-only push (e.g. basic weapon fire). It's checked AFTER the stagger
        // window is opened: the physics-settle window (KnockbackTimer) and the stuck-recovery safety
        // net are independent physical concerns and stay unconditional regardless of whether this
        // specific push is also allowed to cancel the current action.
        public void OnEnemyKnockedBack(Frame f, EntityRef entity, QBoolean canInterrupt)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return;

            if (enemy->Phase == EnemyActionPhase.Dead)
                return;

            // Deliberately BEFORE the CanBeInterruptedByKnockback early-out below: a Heavy/Elite/Boss
            // still physically receives the impulse (this signal only fires once a push actually
            // landed - see DamageUtility.ApplyResolvedImpulse), it just doesn't get staggered by it.
            // It can still be nudged into geometry, so it still wants the safety net.
            EnemyStuckRecoveryUtility.OnKnockedBack(f, entity, enemy);

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);

            if (tierStats.CanBeInterruptedByKnockback == false)
            {
                Log.Debug($"[Knockback] {entity} is not interruptible - AI keeps driving velocity, so the push dies on its next tick");
                return;
            }

            enemy->KnockbackTimer = tierStats.KnockbackRecoveryTime;

            if (canInterrupt == false)
            {
                Log.Debug($"[Knockback] {entity} felt a cosmetic push (staggered, physics only) - source declared canInterrupt=false, action left untouched");
                return;
            }

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

            Log.Debug($"[Knockback] {entity} staggered for {tierStats.KnockbackRecoveryTime}s, Phase {enemy->Phase}");
        }

        // Drops a cancelled windup straight into Recovery, paying this action's full cooldown so a
        // staggered enemy can't immediately re-wind the action it just lost. Deliberately not
        // EnterRecovering - that one calls StopMovement, which would zero the very impulse the
        // stagger exists to preserve. Begin() never ran, so there's no delivery-side cleanup to call.
        // Internal (not private) - EnemyActionUtility.TryInterrupt calls this directly for a
        // pure state-machine cancel with no physics impulse behind it (see that class's own comment).
        internal static void CancelWindup(Frame f, EntityRef entity, Enemy* enemy, EnemyActionData action)
        {
            EnemyDecisionUtility.SetCooldownRemaining(f, entity, enemy, enemy->CurrentActionSlot, action.CooldownTime);
            enemy->StateTimer = action.DownTime;
            enemy->Phase = EnemyActionPhase.Recovery;
        }

        // Active-phase counterpart to CancelWindup - resolves the pointers this signal handler
        // doesn't already have (only Enemy* comes for free here, unlike Update's full Filter) so
        // the interrupted delivery gets a real OnInterrupted call before losing the action. Internal
        // (not private) - EnemyActionUtility.TryInterrupt calls this directly for a pure
        // state-machine cancel with no physics impulse behind it (see that class's own comment).
        internal static void CancelActive(Frame f, EntityRef entity, Enemy* enemy, EnemyDataAsset data, EnemyActionData action)
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
                Log.Debug($"[Enemy] {filter.Entity} detected {target} within DetectionRange={data.AI.ResolveDetectionRange()}, switching Idle -> Chasing");
            }
        }

        // Decoy priority ("max aggro") applies no matter which EnemyTargetingData is configured -
        // it's an override on top of the chosen policy, not baked into any one profile (see
        // EnemyTargetingData's own class comment). Reproduces this system's original (pre-modular)
        // target-acquisition behavior exactly when Targeting is a NearestPlayerTargetingData: decoy
        // first, else whatever the profile picks.
        private static EntityRef ResolveInitialTarget(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            if (EnemyMovementUtility.TryFindNearestDecoy(f, filter.Transform3D->Position, data.AI.ResolveDetectionRange(), out EntityRef decoyTarget) == true)
                return decoyTarget;

            if (data.AI.Targeting.IsValid == false)
                return EntityRef.None;

            return f.FindAsset(data.AI.Targeting).SelectTarget(f, filter.Entity);
        }

        private static void UpdateChasing(Frame f, ref Filter filter, EnemyDataAsset data)
        {
            // A traversal hop in flight (EnemyMovementUtility.BeginTraversalJump) must always
            // finish landing before anything below gets a chance to abandon Chasing - losing the
            // target, exceeding LeashRange, or the target coming into attack range are all
            // perfectly possible mid-hop (a hop closes distance, after all), and none of the other
            // phases ever call MoveInDirection/TickTraversalJump again to complete it. Left
            // unfinished, PhysicsBody3D.IsKinematic (set true by BeginTraversalJump) would never get
            // reset back to false either - Update's own IsKinematic-reset exemption skips it
            // specifically while a hop is in progress - permanently freezing the enemy in place.
            // Ticking it here first, before any of the checks below, guarantees it always lands
            // cleanly (which resets IsKinematic itself) no matter what happens to the target.
            if (EnemyMovementUtility.TickTraversalJump(f, ref filter, data) == true)
                return;

            // Enemy.Target is otherwise fully sticky through Chasing (see this method's own header
            // comment) - a Downed/KO player (see docs/revive.md) has to be treated exactly like a
            // destroyed target here, or an enemy that locked on before its target went down would
            // keep "chasing" someone it can no longer meaningfully attack (Invulnerable) instead of
            // dropping back to Idle and re-acquiring a still-Alive player via its own configured
            // EnemyTargetingData/decoy check.
            if (EnemyMovementUtility.TryGetTargetPosition(f, filter.Enemy->Target, out FPVector3 targetPosition) == false
                || PlayerLifeStateUtility.IsIncapacitated(f, filter.Enemy->Target) == true)
            {
                Log.Debug($"[Enemy] {filter.Entity} lost target {filter.Enemy->Target} (no longer exists or went Downed/KO), switching Chasing -> Idle");
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
            if (EnemyMovementUtility.TryFindNearestDecoy(f, selfPosition, data.AI.ResolveDetectionRange(), out EntityRef decoyTarget) == true && decoyTarget != filter.Enemy->Target)
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

            if (EnemyDecisionUtility.TrySelectAction(f, filter.Entity, filter.Enemy, data, targetPosition, sqrDistance, out EnemyActionData action, out int slot) == true &&
                f.FindAsset(action.Delivery).CanBegin(f, ref filter, data, action, filter.Enemy->Target) == true)
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
                filter.Enemy->SkillTargetPosition = EnemyMovementUtility.ResolveIgnoreY(selfPosition, targetPosition, action.IgnoreY);
                return;
            }

            // Either nothing was eligible, or CanBegin == false (e.g. Charge's dash path is wall/
            // ledge-blocked from here, often a target on a different-height platform) - falls
            // through to the normal chase movement below instead of committing to Preparation, so
            // the enemy keeps closing distance/repositioning and this whole check re-runs next tick.

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

            FP moveSpeed = data.Stats.MoveSpeed * StatusEffectUtility.GetSpeedMultiplier(f, filter.Entity)
                * BossPhaseUtility.ResolveMoveSpeedMultiplier(f, filter.Entity, data);

            // UseWaypointDetour overrides Stats.Movement's own direction only while the direct
            // line to the target is wall-blocked - see EnemyPathfindingUtility.
            // TryGetDetourDirection. Clear line-of-sight, no detour authored, or no route found
            // all fall through to the normal Stats.Movement-computed direction below unchanged.
            if (data.Stats.UseWaypointDetour == false ||
                EnemyPathfindingUtility.TryGetDetourDirection(f, filter.Entity, data, selfPosition, targetPosition, out FPVector2 direction) == false)
            {
                direction = data.Stats.Movement.IsValid == true
                    ? f.FindAsset(data.Stats.Movement).ComputeMoveDirection(f, filter.Entity, filter.Enemy->Target)
                    : default;
            }

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

            // Same reasoning, but for POSITION rather than velocity: zeroing Velocity alone doesn't
            // stop the physics solver from still shoving this enemy sideways if another enemy's
            // collider overlaps it mid-windup, which reads as the telegraphed attack "cheating" -
            // the player dodges the readable spot and gets hit anyway because a bystander bumped it.
            // Going kinematic makes the enemy fully solid/immovable for the windup (still hittable by
            // the player's own knockback/CC, since those are direct writes elsewhere, not solver
            // pushes). Released right before delivery.Begin() below so the delivery itself decides
            // Active-phase kinematic state fresh (Charge/Leap/Burrow set it back to true themselves).
            filter.PhysicsBody3D->IsKinematic = true;

            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, filter.Enemy->CurrentActionSlot);
            EnemyDeliveryData delivery = f.FindAsset(action.Delivery);

            FP windupElapsed = action.AnticipationTime > FP._0
                ? FP._1 - filter.Enemy->StateTimer / action.AnticipationTime
                : FP._1;

            delivery.OnAnticipating(f, ref filter, data, action, filter.Enemy->Target, windupElapsed);

            FP anticipationMultiplier = StatusEffectUtility.GetAnticipationMultiplier(f, filter.Entity)
                * BossPhaseUtility.ResolveAnticipationMultiplier(f, filter.Entity, data);
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

            // Release the telegraph freeze before Begin() runs - Active-phase kinematic state
            // (Charge/Leap/Burrow set it back to true themselves inside Begin(); every other
            // delivery wants it false, same as before this freeze existed) belongs to the delivery,
            // not to the windup that preceded it. If Begin() finishes instantly, EnterRecovering
            // below re-resolves this against Root anyway.
            filter.PhysicsBody3D->IsKinematic = false;

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
                filter.Enemy->SkillTargetPosition = EnemyMovementUtility.ResolveIgnoreY(filter.Transform3D->Position, targetPosition, action.IgnoreY);
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

            // Same "treat a Downed/KO target exactly like a destroyed one" reasoning as
            // UpdateChasing's own check above - see docs/revive.md.
            if (EnemyMovementUtility.TryGetTargetPosition(f, filter.Enemy->Target, out FPVector3 targetPosition) == false
                || PlayerLifeStateUtility.IsIncapacitated(f, filter.Enemy->Target) == true)
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
