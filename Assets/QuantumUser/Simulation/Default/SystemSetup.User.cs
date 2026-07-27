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
            // Seeds CharacterStats/Health from CharacterData the moment a character is created, so
            // it must precede anything reading those stats.
            systems.Add(new CharacterSystem());
            systems.Add(new PlayerInitSystem());
            systems.Add(new KCCSystem());
            // Must run after KCCSystem so it reacts to this tick's freshly-resolved grounded state.
            systems.Add(new AutoJumpSystem());
            // Must run after AutoJumpSystem so LastGroundedPosition reflects this tick's grounded
            // state before checking whether the player has fallen off the level.
            systems.Add(new PlayerFallSystem());
            systems.Add(new AimSystem());
            // Before WeaponSystem, same reason AimSystem itself runs before it - resolves each
            // SentryBarrel's own independent target/Aim/InputSource this tick (see
            // SentryBarrelSystem - every barrel searches from its own position, not a shared
            // chassis target), so WeaponSystem (right after) fires off fresh values, not last tick's.
            systems.Add(new SentryBarrelSystem());
            systems.Add(new WeaponSystem());
            // Must run after KCCSystem (KCC.SetActive/Teleport need this tick's movement already
            // resolved) and after AimSystem (DashSkillData reads Aim.Angle as a facing fallback).
            systems.Add(new SkillSystem());
            systems.Add(new EnemySystem());
            // After EnemySystem so a forced stagger-break action (BossSystem.ForceBreakAction)
            // overrides this tick's Phase/CurrentActionSlot after EnemySystem has already resolved
            // its own decision, rather than being immediately overwritten by it. Only touches
            // entities that actually have BossRuntimeState (see that component's own comment).
            systems.Add(new BossSystem());
            systems.Add(new ProjectileSystem());
            // Must run before AreaDamageSystem so the phase it swaps in for this tick's pulse is
            // what actually gets applied, not last tick's - see AlternatingAreaSystem.
            systems.Add(new AlternatingAreaSystem());
            systems.Add(new AreaDamageSystem());
            // Independent of the area/alternating systems above - no shared state, just needs to run
            // after ProjectileSystem so a vortex spawned this tick can start pulling immediately.
            systems.Add(new VortexSystem());
            // Same reasoning as VortexSystem just above, but also needs to run after SkillSystem
            // (Sentry spawns from there, not ProjectileSystem) - so a settling entity spawned by
            // either path starts easing into position the same tick instead of popping into its
            // final resting spot for one tick first.
            systems.Add(new GroundSettleSystem());
            // No ordering dependency on anything else - just needs to run each tick regardless of
            // whether Brutus is still nearby or using the skill (see JuggernautDischargeCooldown).
            systems.Add(new JuggernautDischargeCooldownSystem());
            // Same reasoning as JuggernautDischargeCooldownSystem just above - ticks independently of
            // whoever applied the mark (see ExplodeOnDeath).
            systems.Add(new ExplodeOnDeathTimerSystem());
            systems.Add(new JuggernautLandingImpactSystem());
            // Also independent - drives JuggernautExplosionPush's own short kinematic move
            // regardless of anything else going on for the enemy (see EnemySystem's own
            // JuggernautExplosionPush exemption for why the two don't fight over IsKinematic).
            systems.Add(new JuggernautExplosionPushSystem());
            // Applies Haste/ShieldRegen to nearby allies - before StatusEffectSystem, same "applied
            // this tick starts ticking next tick" reasoning as every other status-applying system.
            systems.Add(new SentryAuraSystem());
            // Drains a sentry's own Health toward 0 over its lifetime - a real hit as far as
            // DamageUtility is concerned (resets Shield's RechargeTimer like any other), so it must
            // run before StatusEffectSystem/ShieldSystem below for the same reason every other
            // hit-resolving system does.
            systems.Add(new SentryDecaySystem());
            // After every hit-resolving system, so a status applied this tick starts ticking next
            // tick, and before ShieldSystem for the same reason ShieldSystem is documented as late
            // below - a DoT tick landing this frame must hold off shield recharge like any other hit.
            systems.Add(new StatusEffectSystem());
            // Late so a shield never recharges on the same tick a hit landed - DamageUtility has
            // already reset RechargeTimer by the time this runs.
            systems.Add(new ShieldSystem());
            // Must be last: an entity on its final tick still gets to act (a lingering area deals
            // its closing damage) before this destroys it.
            systems.Add(new DestroyAfterTimeSystem());
    }
  }
}