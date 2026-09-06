namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every timed status effect - decrements Remaining timers and fires Burn's damage tick
    // through DamageUtility. Remaining effects going to zero is enough to disable a status;
    // every StatusEffectUtility getter already checks Remaining first, so there's nothing else to
    // clean up here. Runs after WeaponSystem/EnemySystem/ProjectileSystem/AreaDamageSystem (so a
    // status applied this tick starts ticking next tick, same as everything else in this pipeline)
    // and before ShieldSystem, for the same reason ShieldSystem is already documented as late - a DoT
    // tick landing this frame must hold off shield recharge like any other hit.
    [Preserve]
    public unsafe class StatusEffectSystem : SystemMainThreadFilter<StatusEffectSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            StatusEffects* status = filter.StatusEffects;

            TickBurn(f, filter.Entity, status);
            TickElectrified(f, filter.Entity, status);
            TickOverloadChain(f, filter.Entity, status);

            status->IceRemaining -= f.DeltaTime;
            status->StunRemaining -= f.DeltaTime;
            status->RootRemaining -= f.DeltaTime;
            status->RuptureRemaining -= f.DeltaTime;
            status->ShieldRegenRemaining -= f.DeltaTime;
            status->TimeDilationRemaining -= f.DeltaTime;
            status->DamageReductionRemaining -= f.DeltaTime;
            status->AuraDamageReductionRemaining -= f.DeltaTime;
            status->TemporaryDamageReductionRemaining -= f.DeltaTime;
            status->ReactiveDamageReductionCooldownRemaining -= f.DeltaTime;
            status->AllyGuardGrantCooldownRemaining -= f.DeltaTime;
            status->FreeHitGuardRemaining -= f.DeltaTime;
            status->StunImmunityRemaining -= f.DeltaTime;
            status->InterruptImmunityRemaining -= f.DeltaTime;
            status->TempOutgoingDamageRemaining -= f.DeltaTime;
            status->IntimidateRemaining -= f.DeltaTime;
            status->KnockbackTakenRemaining -= f.DeltaTime;
            status->AnticipationSlowRemaining -= f.DeltaTime;
            status->StaggerRemaining -= f.DeltaTime;
            status->ThermalShockCooldownRemaining -= f.DeltaTime;
            status->OverloadCooldownRemaining -= f.DeltaTime;
            status->ShatterCooldownRemaining -= f.DeltaTime;
            status->TemporaryWeaponDamageRemaining -= f.DeltaTime;
            status->RetaliationCooldownRemaining -= f.DeltaTime;
            status->NoAmmoConsumptionRemaining -= f.DeltaTime;
            status->BoundRemaining -= f.DeltaTime;
            status->TempMoveSpeedRemaining -= f.DeltaTime;

            TickHaste(f, status);
            TickCheatDeathImmunity(f, filter.Entity, status);
            TickReviveImmunity(f, filter.Entity, status);

            // Last Stand's cooldown is per-PLAYER, not per-target, so it lives on CharacterStats
            // instead of this component - only players carry CharacterStats, everything else is a
            // no-op TryGetPointer. Reusing this filter's own per-entity iteration (rather than a
            // second dedicated system) since every player already has StatusEffects too.
            if (f.Unsafe.TryGetPointer<CharacterStats>(filter.Entity, out var stats) == true)
            {
                // Rift Mutation per-player timers, on the same reused iteration for the same reason.
                MutationTimerUtility.Tick(f, filter.Entity, stats);
            }
        }

        // Shock/Electrified (Lightning's baseline) - ticks the status duration and, independently,
        // the Jolt interval timer while it's active; on the interval lapsing, applies a brief Stagger
        // (see StatusEffectUtility.ApplyStagger) and resets the interval. Purely deterministic,
        // no proc chance.
        private static void TickElectrified(Frame f, EntityRef entity, StatusEffects* status)
        {
            if (status->ElectrifiedRemaining <= FP._0)
                return;

            status->ElectrifiedRemaining -= f.DeltaTime;
            status->ElectrifiedJoltTimer -= f.DeltaTime;

            if (status->ElectrifiedJoltTimer > FP._0)
                return;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);
            status->ElectrifiedJoltTimer += config != null ? config.JoltInterval : FP._1;

            if (config != null)
                StatusEffectUtility.ApplyStagger(f, entity, config.JoltStaggerDuration);

            // ResolveEntityCenter, not raw Transform3D.Position - that's the ground/feet anchor for
            // most enemies, not the visual body center a VFX should spawn at.
            if (f.Has<Transform3D>(entity) == true)
                f.Events.JoltTriggered(entity, EnemyMovementUtility.ResolveEntityCenter(f, entity));
        }

        // Overload's chain propagates over real simulated time instead of resolving instantly in one
        // frame - HopsRemaining == 0 means no chain is in progress (set by
        // StatusEffectUtility.TryTriggerOverload/TryAdvanceOverloadChain). State lives on the chain's
        // ORIGIN entity regardless of which node the chain's logical position currently sits at.
        private static void TickOverloadChain(Frame f, EntityRef entity, StatusEffects* status)
        {
            if (status->OverloadChainHopsRemaining == 0)
                return;

            status->OverloadChainHopTimer -= f.DeltaTime;

            if (status->OverloadChainHopTimer > FP._0)
                return;

            StatusEffectUtility.TryAdvanceOverloadChain(f, entity, status);
        }

        // Too Angry to Die's brief post-save immunity (see CheatDeathUtility.TryPreventLethal) -
        // unlike the other Remaining fields above (which just gate their own getters once <= 0),
        // this one guards an actual added component (Invulnerable), so lapsing has to explicitly
        // remove it rather than leaving a stale tag sitting behind an expired timer. Same
        // guarded-decrement-then-cleanup shape as TickReviveImmunity below.
        private static void TickCheatDeathImmunity(Frame f, EntityRef entity, StatusEffects* status)
        {
            if (status->CheatDeathImmunityRemaining <= FP._0)
                return;

            status->CheatDeathImmunityRemaining -= f.DeltaTime;

            if (status->CheatDeathImmunityRemaining <= FP._0)
            {
                f.Remove<Invulnerable>(entity);
            }
        }

        // Revive's own post-completion grace window (see PlayerLifeStateUtility.Revive/
        // docs/revive.md) - same guarded-decrement-then-cleanup shape as TickCheatDeathImmunity
        // just above, a second independent reason Invulnerable can be present.
        private static void TickReviveImmunity(Frame f, EntityRef entity, StatusEffects* status)
        {
            if (status->ReviveImmunityRemaining <= FP._0)
                return;

            status->ReviveImmunityRemaining -= f.DeltaTime;

            if (status->ReviveImmunityRemaining <= FP._0)
            {
                f.Remove<Invulnerable>(entity);
            }
        }

        // Each of the 4 slots expires independently - unlike Burn, Haste has no periodic damage
        // tick to fire, so this is just a per-slot decay with no shared timer to coordinate.
        private static void TickHaste(Frame f, StatusEffects* status)
        {
            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0)
                    status->HasteRemaining[i] -= f.DeltaTime;
            }
        }

        private static void TickBurn(Frame f, EntityRef entity, StatusEffects* status)
        {
            if (status->BurnRemaining <= FP._0)
                return;

            status->BurnRemaining -= f.DeltaTime;
            status->BurnTickTimer -= f.DeltaTime;

            if (status->BurnTickTimer > FP._0)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);
            status->BurnTickTimer += config != null ? config.TickInterval : FP._0_50;

            DamageUtility.ApplyDamage(f, entity, status->BurnDamagePerTick, status->BurnOwner,
                status->BurnSource, bypassOutgoingResolution: true, element: ElementType.Fire);

            Log.Debug($"[Status] {entity} Burn ticked for {status->BurnDamagePerTick} ({status->BurnRemaining}s remaining)");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public StatusEffects* StatusEffects;
        }
    }
}
