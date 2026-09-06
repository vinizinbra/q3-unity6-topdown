namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Quantum.Core;

    public static partial class DeterministicSystemSetup
    {
        static partial void AddSystemsUser(ICollection<SystemBase> systems, RuntimeConfig gameConfig, SimulationConfig simulationConfig, SystemsConfig systemsConfig)
        {
            systems.Add(new LevelGenerationSystem());

            // Moved out of the pausable GameplaySystemGroup below (see LevelUpUtility/LevelUpSystem)
            // - must keep reacting to ISignalOnEntityPrototypeMaterialized even while an upgrade
            // screen is open, or a player who spawns mid-screen never gets CharacterStats/Health/
            // Shield seeded (signal dispatch is gated by system-enabled state same as Update - see
            // docs/level-up-upgrades.md). Seeds CharacterStats/Health from CharacterData the moment a
            // character is created, so it must precede anything reading those stats.
            systems.Add(new CharacterSystem());
            systems.Add(new PlayerInitSystem());

            // Nothing needed here for map-baked entities (Chest, BreakableBarrel, ...) any more - a
            // GroundOffset now grounds itself continuously from inside GameplaySystemGroup
            // (GroundSettleSystem, see GroundOffset.qtn), so a hand-placed prop just waits in mid-air
            // until the procedural level actually exists beneath it and then falls. The one-shot
            // MapGroundSettleSystem this replaced could never work: it raycast from
            // ISignalOnEntityPrototypeMaterialized, which fires before LevelGenerationSystem has
            // placed a chunk and before physics has ever built a broadphase.

            // Must react the instant it materializes, regardless of pause state - forces every
            // hand-placed BossArenaGate's collider disabled on creation, so a level designer can't
            // forget to uncheck IsEnabled on one and
            // leave a corridor solid from the start of the run. See docs/run-phase.md's "Boss phase
            // trigger".
            systems.Add(new BossArenaGateSystem());

            // Always-on driver for the Boss encounter's own brief hard pause (see
            // RunPhaseUtility.BeginBossEncounter/GameState.qtn's own BossPauseTimer comment) - same
            // "can't live inside the group it's the one responsible for re-enabling" reasoning
            // LevelUpSystem/ChestSystem/DebugCheatSystem below already document for themselves.
            systems.Add(new BossPauseSystem());

            // Talents (see docs/talents.md) - resolves the shared/coop talent aggregate once, as
            // soon as every connected player has spawned, then spawns whichever Lobby chests were
            // earned directly inside the LobbyStart chunk's own footprint. Must run before
            // ChestSystem below so a chest spawned this tick is already visible to that system's
            // own filter this same tick, and before the pausable group (it's one-shot world
            // bookkeeping, not something that needs to keep reacting through a pause).
            systems.Add(new TalentGateSystem());

            // Always-on driver for the level-up upgrade-choice screen's pause/countdown/resolve -
            // can't live inside the group it disables. See docs/level-up-upgrades.md.
            systems.Add(new LevelUpSystem());

            // Always-on for the same reason as LevelUpSystem just above - a second, independent
            // trigger of Global.LevelUpScreenOpen (via LevelUpUtility.BeginChestScreen) that must
            // keep reacting to its own Chest.Opened flag even while a screen (its own or an
            // Exp-driven one) has GameplaySystemGroup disabled. See docs/chests.md.
            systems.Add(new ChestSystem());

            // Debug-only match-start cheats (see RuntimeConfig.User.cs' DebugStartSurvivalTimeSeconds/
            // DebugStartLevelUpCount and DebugCheatSystem's own comment) - always-on for the same
            // reason as LevelUpSystem/ChestSystem above, so it keeps reacting to a debug-queued level-up
            // screen resolving even while GameplaySystemGroup is disabled for the previous one. Placed
            // right before LobbyBoundarySystem so its one-shot Lobby-skip (if configured) is visible to
            // that system's own gate this same tick.
            systems.Add(new DebugCheatSystem());

            // Handles CheatCommand from the CheatMenu overlay - always-on and OUTSIDE
            // GameplaySystemGroup (like DebugCheatSystem/BossPauseSystem above) so Continue/
            // AdvancePhase still fire while the gameplay group is paused. See CheatSystem.
            systems.Add(new CheatSystem());

            // Lobby Start (see docs/talents.md) - transitions Global.CurrentState from Lobby to
            // Survival once every connected, spawned player has walked outside the LobbyStart
            // chunk's own footprint (no separate boundary entity - the chunk IS the boundary).
            // Must run before CombatDirectorSystem (inside the pausable group just below, later
            // this same tick) so GameState.Survival is current when that system's own gate checks
            // it.
            systems.Add(new LobbyBoundarySystem());

            // Everything below pauses as one unit for the duration of an open level-up screen (see
            // LevelUpUtility.BeginLevelUpScreen/Resolve) - relative order/comments between these
            // systems are unchanged from before, just nested one level deeper.
            systems.Add(new GameplaySystemGroup(
                // First system inside the pausable group; LevelGenerationSystem/CharacterSystem/
                // PlayerInitSystem/LevelUpSystem all run every tick regardless of pause state,
                // immediately before this. Same reasoning as always for running this early: an enemy
                // this purchases this tick is already inside EnemySystem's filter for this same
                // Update() call, so it gets its first AI decision the instant it's born instead of
                // waiting a full tick. Reads player position/velocity as of last tick's resolved
                // values (runs before KCCSystem) - fine, since the "moving combat bubble" prediction
                // is only recomputed once per Director pulse, every few seconds.
                // Also drives the Combat<->Breathing loop now (Breathing is just another entry in
                // SurvivalConfig.Phases[], see docs/run-phase.md) - no separate system needed.
                new CombatDirectorSystem(),
                // Synthesizes this tick's Input for every RuntimePlayer.IsBot slot (see
                // BotBrain.qtn / docs/bots.md) - immediately before KCCSystem so the decision it
                // makes is the one this same tick's movement resolves, rather than landing a tick
                // late. Inside the pausable group deliberately: a bot should freeze along with
                // everyone else while an upgrade screen is open.
                new BotInputSystem(),
                new KCCSystem(),
                // Must run after KCCSystem so it reacts to this tick's freshly-resolved grounded state.
                new AutoJumpSystem(),
                // Must run after AutoJumpSystem so LastGroundedPosition reflects this tick's grounded
                // state before checking whether the player has fallen off the level.
                new PlayerFallSystem(),
                new AimSystem(),
                // Right after AimSystem so it reads this tick's fresh Aim.Target - advances the
                // "hold the reticle on a barrel for BreakDelay seconds" dwell for any Breakable a
                // player is auto-targeting, and breaks it once the dwell completes. See
                // BreakableFocusSystem/Breakable.qtn.
                new BreakableFocusSystem(),
                // Before WeaponSystem, same reason AimSystem itself runs before it - resolves each
                // SentryBarrel's own independent target/Aim/InputSource this tick (see
                // SentryBarrelSystem - every barrel searches from its own position, not a shared
                // chassis target), so WeaponSystem (right after) fires off fresh values, not last tick's.
                new SentryBarrelSystem(),
                new WeaponSystem(),
                // Reacts to Combat.qtn's OnEntityKilled/OnCriticalHit/OnWeaponHitLanded signals
                // WeaponSystem's own fire path can dispatch via DamageUtility - the on-kill/on-crit/
                // ramp side of the weapon-perk roster (Killer Instinct, Predator Magazine,
                // Bottomless Momentum, Critical Rebound, the shared ramp pool). See
                // docs/weapon-perks.md.
                new WeaponPerkReactionSystem(),
                // Reacts to Combat.qtn's OnCriticalHit, CharacterSkills.qtn's OnSkillActivated, and
                // Shield.qtn's OnShieldBroken - the crit/dash-activation/shield-break side of the
                // Rift Mutation roster (Critical Focus, Infinite Momentum, Shield Breaker). See
                // docs/rift-mutations.md.
                new RiftMutationReactionSystem(),
                // Max's Overdrive Ascension reactions - Uncontrolled Fury's capped per-N-kills
                // extension (Vendetta kills included), Ignition rank 2's Burning Ground drop, Blood
                // Debt rank 2's Rage refund, and Rage's own loss-on-damage. MUST run BEFORE
                // MaxVendettaSystem just below - two of those reactions read RevengeMark.MarkedBy on
                // the same OnEntityKilled dispatch MaxVendettaSystem's own handler then removes the
                // mark on. See docs/max-ascensions.md.
                new MaxOverdriveReactionSystem(),
                // Max's Vendetta passive - reacts to Combat.qtn's OnHealthDamageApplied/
                // OnShieldDamageApplied/OnEntityKilled (mark creation/accumulation/consumption+heal).
                // See docs/max-ascensions.md.
                new MaxVendettaSystem(),
                // Independent per-tick ticker for every active RevengeMark's own countdown, same
                // "needs to keep counting down regardless of anything else" reasoning
                // ExplodeOnDeathTimerSystem's own comment gives.
                new RevengeMarkTimeoutSystem(),
                // Kai's First Strike rank 3 - banks a one-shot bonus onto his next First Strike when
                // he kills a First-Strike-marked target. Replaces FirstStrikeMarkTimeoutSystem (the
                // refresh-window mechanism it ticked was removed - each enemy triggers First Strike
                // exactly once now). Signal-driven, no ordering dependency, same shape as
                // MaxVendettaSystem.
                new KaiFirstStrikeSystem(),
                // Max's Fire Mastery traits - reacts to Combat.qtn's OnCriticalHit/
                // OnHealthDamageApplied/OnEntityKilled (Flashpoint/Cremation/Wildfire), plus its own
                // per-tick ExplosionOnConditionalHit.CooldownRemaining ticking. Independent of
                // MaxVendettaSystem above - see docs/max-ascensions.md.
                new MaxFireMasteryReactionSystem(),
                // Pixie's Pocket Bombs Ascension - reacts to Combat.qtn's
                // OnAreaExplosionDetonated. Direct Hit is NOT here - it hooks directly into
                // HitEffectUtility.ApplyInRadius/ApplyDamageInRadius instead (see
                // DemolitionMasteryUtility), since it needs the blast's center/radius, not just a
                // signal payload.
                new PixieDemolitionMasterySystem(),
                // Resolves each player's own ContextInteraction.ActiveTarget (Base-Skill-button
                // redirect, see ContextInteraction.qtn/docs/breathing-poi.md) - must run before
                // SkillSystem, which reads it the same tick to decide whether a Hero Skill press
                // casts the real skill or opens a Cursed Rift interaction instead.
                new ContextInteractionSystem(),
                // Must run after KCCSystem (KCC.SetActive/Teleport need this tick's movement already
                // resolved) and after AimSystem (DashSkillData reads Aim.Angle as a facing fallback).
                new SkillSystem(),
                // Hold-to-revive (see docs/revive.md) - PlayerLifeStateSystem ticks a Downed
                // player's own bleed-out timer (must run after SkillSystem so a same-tick
                // TryBeginInteraction that just set ReviveHolder is already reflected before this
                // decides whether to tick this tick) AND processes this player's own
                // SelfReviveCommand (a separate, instant press/confirm - no channel/hold involved).
                // ReviveChannelSystem ticks every actively-holding TEAMMATE reviver's own channel,
                // right after it for the same same-tick-consistency reason. ReviveDamageInterruptSystem
                // is signal-driven (OnHealthDamageApplied/OnShieldDamageApplied fire synchronously
                // from wherever DamageUtility.ApplyDamage is actually called, not from this position
                // in the list) - grouped here purely for discoverability. RunFailureSystem is last
                // so it reads this tick's fully-resolved life-state.
                new PlayerLifeStateSystem(),
                new ReviveChannelSystem(),
                new ReviveDamageInterruptSystem(),
                new RunFailureSystem(),
                // Debug-only command processor for LevelUpPoolKind.PassiveUpgrade (see
                // PassiveUpgradeSystem's own comment) - before VoidFieldSystem so a same-tick-granted
                // Void Pressure ascension is already reflected when that system runs this same tick.
                new PassiveUpgradeSystem(),
                // Debug-only command processor for LevelUpPoolKind.GlobalUpgrade - same reasoning and
                // placement as PassiveUpgradeSystem just above (see GlobalUpgradeSystem's own comment).
                new GlobalUpgradeSystem(),
                // Debug-only command processor for LevelUpPoolKind.RiftMutation - same reasoning and
                // placement as GlobalUpgradeSystem just above (see RiftMutationSystem's own comment).
                new RiftMutationSystem(),
                // Command-processing driver for Cursed Rift's own two-step sacrifice/mutation
                // interaction (see CursedRiftUtility/docs/breathing-poi.md) - placed alongside
                // RiftMutationSystem since it's the same "debug/command processor for a
                // LevelUp-adjacent pool" shape, though CursedRiftSystem is player-triggered, not
                // debug-only.
                new CursedRiftSystem(),
                // Command-processing drivers for Store/Blacksmith's own interactions (see
                // StoreUtility/BlacksmithUtility/docs/store-blacksmith.md) - same "debug/command
                // processor for a LevelUp-adjacent pool" shape as CursedRiftSystem just above.
                new StoreSystem(),
                new BlacksmithSystem(),
                // After SkillSystem (so a SlowArea entity spawned this same tick already exists here)
                // and before both EnemySystem and ProjectileSystem (so a same-tick-fresh
                // TimeDilation/SpeedMultiplier is what their own Tick/Update calls read this tick, not
                // last tick's stale value) - see VoidFieldSystem's own comment.
                new VoidFieldSystem(),
                new EnemySystem(),
                // After EnemySystem so a forced stagger-break action (BossSystem.ForceBreakAction)
                // overrides this tick's Phase/CurrentActionSlot after EnemySystem has already resolved
                // its own decision, rather than being immediately overwritten by it. Only touches
                // entities that actually have BossRuntimeState (see that component's own comment).
                new BossSystem(),
                // Boss/Elite-only equivalent of PlayerFallSystem (see its own header comment) -
                // right after EnemySystem/BossSystem so it reads this tick's resolved
                // Transform3D.Position before anything else acts on a freshly-respawned position.
                new EnemyFallSystem(),
                // Elite-only "never forgotten" relocation - same "read this tick's resolved
                // position before anything else acts on it" placement as EnemyFallSystem right
                // above, and must run after it so a same-tick fall-respawn is what this reads too.
                new EliteRelocationSystem(),
                new ProjectileSystem(),
                // Must run before AreaDamageSystem so the phase it swaps in for this tick's pulse is
                // what actually gets applied, not last tick's - see AlternatingAreaSystem.
                new AlternatingAreaSystem(),
                new AreaDamageSystem(),
                // Independent of the area/alternating systems above - no shared state, just needs to run
                // after ProjectileSystem so a vortex spawned this tick can start pulling immediately.
                new VortexSystem(),
                // Kai's Undertow Ascension - its own pull (DamageUtility.ApplyPull) needs the exact
                // same after-EnemySystem placement as VortexSystem just above, for the exact same
                // reason: EnemySystem writes PhysicsBody3D.Velocity every tick regardless of phase,
                // which would otherwise erase this impulse before it moved anything. See
                // docs/kai-ascensions.md.
                new KaiUndertowSystem(),
                // Same reasoning as VortexSystem just above, but also needs to run after SkillSystem
                // (Sentry spawns from there, not ProjectileSystem) - so a settling entity spawned by
                // either path starts easing into position the same tick instead of popping into its
                // final resting spot for one tick first.
                new GroundSettleSystem(),
                // No ordering dependency on anything else - a popped orb (OrbSpawnUtility.SpawnWithPop)
                // just needs to keep integrating its own arc every tick regardless of what else ran.
                new PopMotionSystem(),
                // No ordering dependency on anything else - marks Chunk.Discovered as players walk
                // into a chunk's footprint, feeding the minimap's "?" -> real icon reveal. See
                // docs/minimap.md.
                new ChunkDiscoverySystem(),
                // No ordering dependency on anything else - just needs to run each tick regardless of
                // whether Brutus is still nearby or using the skill (see JuggernautDischargeCooldown).
                new JuggernautDischargeCooldownSystem(),
                // Same reasoning as JuggernautDischargeCooldownSystem just above - ticks independently of
                // whoever applied the mark (see ExplodeOnDeath).
                new ExplodeOnDeathTimerSystem(),
                // Same shape as ExplodeOnDeathTimerSystem just above - ticks down Pixie's shared
                // next-Bunny-Bomb charge (Hot Fuse + Blast Jump) regardless of anything else, so an
                // unused charge expires instead of lingering forever (see PixieBombCharge.qtn).
                new PixieBombChargeSystem(),
                // Generic one-shot delayed area blast - Pixie's Unstable Mixture rank 3 secondary
                // explosion and Brute's Aftershock rank 3 "Earthquake" both schedule through it.
                // Independent countdown, no ordering dependency, same reasoning as every other
                // countdown system here.
                new DelayedBlastSystem(),
                new JuggernautLandingImpactSystem(),
                // Also independent - drives JuggernautExplosionPush's own short kinematic move
                // regardless of anything else going on for the enemy (see EnemySystem's own
                // JuggernautExplosionPush exemption for why the two don't fight over IsKinematic).
                new JuggernautExplosionPushSystem(),
                // Applies Haste/ShieldRegen to nearby allies - before StatusEffectSystem, same "applied
                // this tick starts ticking next tick" reasoning as every other status-applying system.
                // Brute's Protector Aura - same continuous-refresh shape as SentryAuraSystem just
                // below, placed alongside it for the same reason.
                new ProtectorAuraSystem(),
                // Guardian ascension rank 3's reactive DR proc - reacts to Combat.qtn's
                // OnHealthDamageApplied/OnShieldDamageApplied, same signal-driven "no ordering
                // dependency" shape as MaxVendettaSystem.
                new BruteProtectorReactionSystem(),
                // Bodyguard ascension ranks 2-3's payout - reacts to Combat.qtn's
                // OnFreeHitGuardConsumed, so like the two reactions around it it has no ordering
                // dependency of its own and is placed here purely to keep Brute's reaction systems
                // together.
                new BruteBodyguardReactionSystem(),
                // Brute's Groundbreaker ascension - reacts to the generic OnPlayerLanded signal
                // (PlayerMovement.qtn) rather than ticking, so it has no ordering dependency of its
                // own; placed alongside his other Protector-pool reaction, and before
                // StatusEffectSystem for the same "applied this tick starts ticking next tick" reason
                // every other status-applying system is.
                new BruteGroundbreakerSystem(),
                // Zara's Afterbeat dash ascension - no ordering dependency on anything else, same
                // reasoning as JuggernautDischargeCooldownSystem's own placement comment.
                new ZaraAfterbeatSystem(),
                // Zara's Flow State - ticks her movement/build/decay clock and reacts to Combat.qtn's
                // OnHostileHitConnected to break it. Placed before StatusEffectSystem for the same
                // "applied this tick starts ticking next tick" reason every status-applying system is,
                // and it is filtered on ZaraFlow so it costs nothing for any other hero.
                new ZaraFlowSystem(),
                new SentryAuraSystem(),
                // Drains a sentry's own Health toward 0 over its lifetime - a real hit as far as
                // DamageUtility is concerned (resets Shield's RechargeTimer like any other), so it must
                // run before StatusEffectSystem/ShieldSystem below for the same reason every other
                // hit-resolving system does.
                new SentryDecaySystem(),
                // After every hit-resolving system, so a status applied this tick starts ticking next
                // tick, and before ShieldSystem for the same reason ShieldSystem is documented as late
                // below - a DoT tick landing this frame must hold off shield recharge like any other hit.
                new StatusEffectSystem(),
                // Late so a shield never recharges on the same tick a hit landed - DamageUtility has
                // already reset RechargeTimer by the time this runs.
                new ShieldSystem(),
                // Same "after every hit-resolving system" placement as ShieldSystem just above, though
                // regen itself has no on-hit delay to protect - see HealthRegenSystem.
                new HealthRegenSystem(),
                // Independent of everything else - just needs to run before DestroyAfterTimeSystem,
                // preserving that system's own "must be last" invariant since this also calls
                // f.Destroy. Also must stay inside this group: for an Experience-type orb this is
                // the only caller of ExperienceUtility.Grant, and Grant is what triggers a new
                // screen - keeping it paused while one is already open is what makes re-entrancy
                // structurally impossible (see docs/level-up-upgrades.md). Replaces what used to be
                // 3 separate systems (ExpOrbSystem/CoinOrbSystem/RiftShardOrbSystem) - only
                // ExpOrbSystem was actually registered here before this merge, so Coin/RiftShard
                // pickups now actually collect for the first time.
                new CurrencyOrbSystem(),
                // Same reasoning as CurrencyOrbSystem just above (must stay inside this group, must
                // run before DestroyAfterTimeSystem since it also calls f.Destroy) - Scrap has no
                // re-entrancy concern of its own (unlike Grant/BeginLevelUpScreen), but there's no
                // reason to place it anywhere else either.
                new ScrapOrbSystem(),
                // Same shape as CurrencyOrbSystem (walk-into-radius collect, runs before
                // DestroyAfterTimeSystem since it also f.Destroys) - collects HealthOrbs dropped by a
                // Breakable's loot table and heals the collecting player. See HealthOrbSystem.
                new HealthOrbSystem(),
                // Same walk-into-radius/f.Destroy-before-DestroyAfterTimeSystem shape as the three
                // pickup systems just above, with one deliberate difference: only the accessory's
                // OWNER can collect it, so it scans one player instead of all of them. Also drives
                // the popped-off accessory's own Airborne -> Dropped landing transition (read off
                // PopVelocity, which PopMotionSystem removes on landing much earlier this same tick).
                // See docs/accessory-guard.md.
                new AccessoryGuardSystem(),
                // Healing Shrine has no system of its own - it's press-to-heal via the same
                // Base-Skill-button redirect Cursed Rift uses (HealingShrineUtility.TryInteract,
                // called from SkillSystem), not a walk-into-radius system - see docs/breathing-poi.md.
                // After SkillSystem AND CursedRiftSystem (both much earlier, near RiftMutationSystem/
                // ContextInteractionSystem) so a usage marked by either this same tick is already
                // reflected in PoiActivation.State this same tick, not one tick stale.
                new PoiActivationSystem(),
                // Ticks each Active Traversal Challenge's own countdown/checkpoint check - lives
                // inside this group (unlike BossPauseSystem/ChestSystem) since it never itself
                // disables/enables GameplaySystemGroup; its global pause instead goes through
                // Global.ActiveTraversalChallengeCount, checked by CombatDirectorSystem/
                // SurvivalProgressionUtility much earlier in this same list. See
                // docs/traversal-challenge.md.
                new TraversalChallengeSystem(),
                // After every hit-resolving system above (this tick's Enemy.Phase is fully settled, so
                // a same-tick combat death is correctly excluded from retirement/refund) and before
                // DestroyAfterTimeSystem, preserving that system's own "must be last" invariant since
                // this also calls f.Destroy.
                new EnemyLifecycleSystem(),
                // Must be last: an entity on its final tick still gets to act (a lingering area deals
                // its closing damage) before this destroys it - and, for anything carrying
                // ExplodeOnDestroy (Pixie's Dash Ascension "Leave Explosive Bomb", so far), detonates
                // the instant before being destroyed (see DestroyAfterTimeSystem's own optional
                // check, ExplodeOnDestroy.qtn).
                new DestroyAfterTimeSystem()
            ));
    }
  }
}