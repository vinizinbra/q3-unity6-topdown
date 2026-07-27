namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every timed status effect - decrements Remaining timers and fires Burn/Poison damage
    // ticks through DamageUtility. Remaining effects going to zero is enough to disable a status;
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
            TickPoison(f, filter.Entity, status);

            status->IceRemaining -= f.DeltaTime;
            status->StunRemaining -= f.DeltaTime;
            status->RootRemaining -= f.DeltaTime;
            status->MarkRemaining -= f.DeltaTime;
            status->ShieldRegenRemaining -= f.DeltaTime;

            TickHaste(f, status);
        }

        // Each of the 4 slots expires independently - unlike Poison, Haste has no periodic damage
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

            status->BurnTickTimer += StatusEffectUtility.TickInterval;

            DamageUtility.ApplyDamage(f, entity, status->BurnDamagePerTick, status->BurnOwner,
                status->BurnSource, bypassOutgoingResolution: true, element: ElementType.Fire);

            Log.Debug($"[Status] {entity} Burn ticked for {status->BurnDamagePerTick} ({status->BurnRemaining}s remaining)");
        }

        // Each of the 5 slots expires independently, but they all tick on one shared timer (see
        // StatusEffectUtility.ApplyPoison) - so this fires every active slot together instead of
        // however many happen to land on the same frame.
        private static void TickPoison(Frame f, EntityRef entity, StatusEffects* status)
        {
            bool anyActive = false;

            for (int i = 0; i < 5; i++)
            {
                if (status->PoisonRemaining[i] <= FP._0)
                    continue;

                status->PoisonRemaining[i] -= f.DeltaTime;
                anyActive = true;
            }

            if (anyActive == false)
                return;

            status->PoisonTickTimer -= f.DeltaTime;

            if (status->PoisonTickTimer > FP._0)
                return;

            status->PoisonTickTimer += StatusEffectUtility.TickInterval;

            ApplyPoisonTicks(f, entity, status);
        }

        // Sums every active stack's damage per owner, so your own re-applied poison merges into one
        // bigger hit/number (see DamageFeedbackManager) while another player's poison on the same
        // target still lands - and reads - as its own hit on the same beat.
        private static void ApplyPoisonTicks(Frame f, EntityRef entity, StatusEffects* status)
        {
            EntityRef* groupOwner = stackalloc EntityRef[5];
            DamageSource* groupSource = stackalloc DamageSource[5];
            FP* groupDamage = stackalloc FP[5];
            int groupCount = 0;

            for (int i = 0; i < 5; i++)
            {
                if (status->PoisonRemaining[i] <= FP._0)
                    continue;

                EntityRef owner = status->PoisonOwner[i];
                int group = -1;

                for (int g = 0; g < groupCount; g++)
                {
                    if (groupOwner[g] == owner)
                    {
                        group = g;
                        break;
                    }
                }

                if (group == -1)
                {
                    group = groupCount++;
                    groupOwner[group] = owner;
                    groupSource[group] = status->PoisonSource[i];
                    groupDamage[group] = FP._0;
                }

                groupDamage[group] += status->PoisonDamagePerTick[i];
            }

            for (int g = 0; g < groupCount; g++)
            {
                DamageUtility.ApplyDamage(f, entity, groupDamage[g], groupOwner[g], groupSource[g],
                    bypassOutgoingResolution: true, element: ElementType.Poison);

                Log.Debug($"[Status] {entity} Poison ticked for {groupDamage[g]} from {groupOwner[g]} " +
                          $"({groupCount} owner group(s) this beat)");
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public StatusEffects* StatusEffects;
        }
    }
}
