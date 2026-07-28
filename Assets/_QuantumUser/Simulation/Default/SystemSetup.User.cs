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

            // Always-on driver for the level-up upgrade-choice screen's pause/countdown/resolve -
            // can't live inside the group it disables. See docs/level-up-upgrades.md.
            systems.Add(new LevelUpSystem());

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
                new CombatDirectorSystem(),
                new KCCSystem(),
                // Must run after KCCSystem so it reacts to this tick's freshly-resolved grounded state.
                new AutoJumpSystem(),
                // Must run after AutoJumpSystem so LastGroundedPosition reflects this tick's grounded
                // state before checking whether the player has fallen off the level.
                new PlayerFallSystem(),
                new AimSystem(),
                // Before WeaponSystem, same reason AimSystem itself runs before it - resolves each
                // SentryBarrel's own independent target/Aim/InputSource this tick (see
                // SentryBarrelSystem - every barrel searches from its own position, not a shared
                // chassis target), so WeaponSystem (right after) fires off fresh values, not last tick's.
                new SentryBarrelSystem(),
                new WeaponSystem(),
                // Must run after KCCSystem (KCC.SetActive/Teleport need this tick's movement already
                // resolved) and after AimSystem (DashSkillData reads Aim.Angle as a facing fallback).
                new SkillSystem(),
                new EnemySystem(),
                // After EnemySystem so a forced stagger-break action (BossSystem.ForceBreakAction)
                // overrides this tick's Phase/CurrentActionSlot after EnemySystem has already resolved
                // its own decision, rather than being immediately overwritten by it. Only touches
                // entities that actually have BossRuntimeState (see that component's own comment).
                new BossSystem(),
                new ProjectileSystem(),
                // Must run before AreaDamageSystem so the phase it swaps in for this tick's pulse is
                // what actually gets applied, not last tick's - see AlternatingAreaSystem.
                new AlternatingAreaSystem(),
                new AreaDamageSystem(),
                // Independent of the area/alternating systems above - no shared state, just needs to run
                // after ProjectileSystem so a vortex spawned this tick can start pulling immediately.
                new VortexSystem(),
                // Same reasoning as VortexSystem just above, but also needs to run after SkillSystem
                // (Sentry spawns from there, not ProjectileSystem) - so a settling entity spawned by
                // either path starts easing into position the same tick instead of popping into its
                // final resting spot for one tick first.
                new GroundSettleSystem(),
                // No ordering dependency on anything else - just needs to run each tick regardless of
                // whether Brutus is still nearby or using the skill (see JuggernautDischargeCooldown).
                new JuggernautDischargeCooldownSystem(),
                // Same reasoning as JuggernautDischargeCooldownSystem just above - ticks independently of
                // whoever applied the mark (see ExplodeOnDeath).
                new ExplodeOnDeathTimerSystem(),
                new JuggernautLandingImpactSystem(),
                // Also independent - drives JuggernautExplosionPush's own short kinematic move
                // regardless of anything else going on for the enemy (see EnemySystem's own
                // JuggernautExplosionPush exemption for why the two don't fight over IsKinematic).
                new JuggernautExplosionPushSystem(),
                // Applies Haste/ShieldRegen to nearby allies - before StatusEffectSystem, same "applied
                // this tick starts ticking next tick" reasoning as every other status-applying system.
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
                // Independent of everything else - just needs to run before DestroyAfterTimeSystem,
                // preserving that system's own "must be last" invariant since this also calls
                // f.Destroy. Also must stay inside this group: ExpOrbSystem is the only caller of
                // ExperienceUtility.Grant, and Grant is what triggers a new screen - keeping it
                // paused while one is already open is what makes re-entrancy structurally impossible
                // (see docs/level-up-upgrades.md).
                new ExpOrbSystem(),
                // After every hit-resolving system above (this tick's Enemy.Phase is fully settled, so
                // a same-tick combat death is correctly excluded from retirement/refund) and before
                // DestroyAfterTimeSystem, preserving that system's own "must be last" invariant since
                // this also calls f.Destroy.
                new EnemyLifecycleSystem(),
                // Must be last: an entity on its final tick still gets to act (a lingering area deals
                // its closing damage) before this destroys it.
                new DestroyAfterTimeSystem()
            ));
    }
  }
}