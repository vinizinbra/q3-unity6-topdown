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

            status->IceRemaining -= f.DeltaTime;
            status->StunRemaining -= f.DeltaTime;
            status->RootRemaining -= f.DeltaTime;
            status->BreakRemaining -= f.DeltaTime;
            status->ShieldRegenRemaining -= f.DeltaTime;
            status->TimeDilationRemaining -= f.DeltaTime;
            status->DamageReductionRemaining -= f.DeltaTime;
            status->IntimidateRemaining -= f.DeltaTime;
            status->KnockbackTakenRemaining -= f.DeltaTime;
            status->VoidRemaining -= f.DeltaTime;
            status->AnticipationSlowRemaining -= f.DeltaTime;
            status->ExplosionCooldownRemaining -= f.DeltaTime;
            status->FreezeCooldownRemaining -= f.DeltaTime;
            status->KnockbackCooldownRemaining -= f.DeltaTime;
            status->MagmaPrisonCooldownRemaining -= f.DeltaTime;
            status->StunCooldownRemaining -= f.DeltaTime;
            status->BreakCooldownRemaining -= f.DeltaTime;

            TickHaste(f, status);
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
