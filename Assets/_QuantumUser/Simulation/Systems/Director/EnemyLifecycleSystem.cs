namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Domain 3 (Enemy Lifecycle) - one system for relevance/retirement/refund, same reasoning
    // EnemySystem itself doesn't split its own Idle-> ... ->Dead state machine across systems:
    // these are sequential steps of one per-entity state advance, once per tick, over the same
    // component. Only touches entities that actually have EnemyLifecycle - see that component's
    // own comment (opt-in, added by CombatDirectorUtility at spawn time).
    //
    // Placed right before DestroyAfterTimeSystem, after every hit-resolving system
    // (ProjectileSystem/AreaDamageSystem/VortexSystem/StatusEffectSystem/ShieldSystem) - this sees
    // this tick's fully-resolved Enemy.Phase (a same-tick combat death is correctly excluded, not
    // one-tick-stale) and preserves DestroyAfterTimeSystem's own documented "must be last"
    // invariant, since this system also calls f.Destroy.
    [Preserve]
    public unsafe class EnemyLifecycleSystem : SystemMainThreadFilter<EnemyLifecycleSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            // A combat kill is already EnemySystem/DamageUtility's own path (OnEnemyDied,
            // DeathLingerTime) - retirement must never double-manage that entity or refund budget
            // for something players actually killed, which would let players farm budget by
            // killing Director-spawned trash.
            if (filter.Enemy->Phase == EnemyActionPhase.Dead)
                return;

            if (f.RuntimeConfig.LifecycleConfig.Id.IsValid == false)
                return;

            LifecycleConfig lifecycleConfig = f.FindAsset(f.RuntimeConfig.LifecycleConfig);
            EnemyDataAsset data = f.FindAsset(filter.Enemy->EnemyData);

            UpdateRecentCombat(f, ref filter, lifecycleConfig);
            AdvanceState(f, ref filter, data, lifecycleConfig);

            if (filter.EnemyLifecycle->State == EnemyLifecycleState.Retired)
            {
                Retire(f, ref filter, data, lifecycleConfig);
            }
        }

        private static void AdvanceState(Frame f, ref Filter filter, EnemyDataAsset data, LifecycleConfig lifecycleConfig)
        {
            bool relevant = IsRelevant(f, ref filter, data, lifecycleConfig);

            if (relevant == true)
            {
                filter.EnemyLifecycle->State = EnemyLifecycleState.Active;
                filter.EnemyLifecycle->IrrelevantTimer = FP._0;
                return;
            }

            switch (filter.EnemyLifecycle->State)
            {
                case EnemyLifecycleState.Active:
                    filter.EnemyLifecycle->State = EnemyLifecycleState.Irrelevant;
                    filter.EnemyLifecycle->IrrelevantTimer = FP._0;
                    break;

                case EnemyLifecycleState.Irrelevant:
                    filter.EnemyLifecycle->IrrelevantTimer += f.DeltaTime;

                    if (filter.EnemyLifecycle->IrrelevantTimer >= lifecycleConfig.RetireDelay)
                    {
                        filter.EnemyLifecycle->State = EnemyLifecycleState.Retired;
                    }

                    break;
            }
        }

        // Flat OR of five named conditions, no scoring - a dev debugging a retirement decision
        // only needs to check these, not a hidden weighted formula. Persistent being just one term
        // here is the entire implementation of "persistent enemies never auto-retire" - no
        // special-cased branch anywhere else.
        private static bool IsRelevant(Frame f, ref Filter filter, EnemyDataAsset data, LifecycleConfig lifecycleConfig)
        {
            if (data.Economy.Persistent == true)
                return true;

            if (data.Tier == EnemyTier.Elite)
                return true;

            if (filter.EnemyLifecycle->RecentCombatTimer > FP._0)
                return true;

            if (IsAttacking(filter.Enemy->Phase) == true)
                return true;

            return IsCloseToAnyPlayer(f, filter.Transform3D->Position, lifecycleConfig.RelevantRange);
        }

        private static bool IsAttacking(EnemyActionPhase phase)
        {
            return phase == EnemyActionPhase.Preparation
                || phase == EnemyActionPhase.Telegraph
                || phase == EnemyActionPhase.Execute
                || phase == EnemyActionPhase.Active
                || phase == EnemyActionPhase.Recovery;
        }

        // Plain loop over live PlayerLink entities, not a physics overlap query - for 2-4 players
        // a direct compare beats query/layer-mask setup overhead.
        private static bool IsCloseToAnyPlayer(Frame f, FPVector3 position, FP range)
        {
            FP rangeSqr = range * range;
            var filtered = f.Filter<PlayerLink, Transform3D>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _, out Transform3D transform))
            {
                if (EnemyMovementUtility.FlatSqrDistance(position, transform.Position) <= rangeSqr)
                    return true;
            }

            return false;
        }

        // Same no-dedicated-signal trick BossRuntimeState.LastObservedHealth already uses -
        // diffed against Health.CurrentHealth each tick instead of hooking DamageUtility directly.
        private static void UpdateRecentCombat(Frame f, ref Filter filter, LifecycleConfig lifecycleConfig)
        {
            bool tookDamage = false;

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == true)
            {
                FP lastHealth = filter.EnemyLifecycle->LastObservedHealth;

                // First tick after spawn has nothing to diff against yet - seed the baseline
                // instead of reading a damage spike out of the initial (0 -> MaxHealth) jump.
                if (lastHealth > FP._0 && health->CurrentHealth < lastHealth)
                {
                    tookDamage = true;
                }

                filter.EnemyLifecycle->LastObservedHealth = health->CurrentHealth;
            }

            if (tookDamage == true || IsAttacking(filter.Enemy->Phase) == true)
            {
                filter.EnemyLifecycle->RecentCombatTimer = lifecycleConfig.RecentCombatWindow;
            }
            else
            {
                filter.EnemyLifecycle->RecentCombatTimer = FPMath.Max(FP._0, filter.EnemyLifecycle->RecentCombatTimer - f.DeltaTime);
            }
        }

        private static void Retire(Frame f, ref Filter filter, EnemyDataAsset data, LifecycleConfig lifecycleConfig)
        {
            FP refund = data.ResolveCost(f) * lifecycleConfig.RefundFraction;
            f.Global->DirectorBudget += refund;

            Log.Debug($"[Director] retiring {filter.Entity} ({data.name}) - refunding {refund}, DirectorBudget now {f.Global->DirectorBudget}");

            f.Destroy(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Enemy* Enemy;
            public EnemyLifecycle* EnemyLifecycle;
            public Transform3D* Transform3D;
        }
    }
}
