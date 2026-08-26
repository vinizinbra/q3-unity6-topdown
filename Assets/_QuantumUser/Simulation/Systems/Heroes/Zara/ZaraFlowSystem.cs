namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives Zara's Flow State bar - fill while moving, hold briefly when she stops, then drain - and
    // reacts to the generic "a hostile attack connected" signal to break it.
    //
    // Filtered on ZaraFlow, so it no-ops entirely for every other hero and each Zara in a co-op match
    // ticks her own component with zero shared or static state.
    //
    // Every timer is FP against f.DeltaTime; nothing here reads Unity time.
    [Preserve]
    public unsafe class ZaraFlowSystem : SystemMainThreadFilter<ZaraFlowSystem.Filter>, ISignalOnHostileHitConnected
    {
        public override void Update(Frame f, ref Filter filter)
        {
            ZaraFlow* flow = filter.Flow;

            TickCooldowns(f, flow);
            RefreshActiveDamageBonus(f, filter.Entity, flow);

            flow->IsMoving = ResolveIsMoving(f, filter.Entity, flow);

            if (flow->IsMoving == true)
            {
                // A single moving tick cancels a drain in progress - "movement immediately stops
                // further decay".
                flow->StationaryTimer = FP._0;
                TickBuild(f, filter.Entity, flow);
                return;
            }

            TickStationary(f, filter.Entity, flow);
        }

        // Headliner rank 1 - kept alive by refreshing the generic timed outgoing-damage slot every tick
        // she is Active, the same continuous-refresh idiom every aura in this project uses
        // (ProtectorAuraSystem, SentryAuraSystem). Needs no removal path: stop refreshing and it lapses
        // on its own a beat later.
        private static void RefreshActiveDamageBonus(Frame f, EntityRef entity, ZaraFlow* flow)
        {
            FP bonus = ZaraFlowUtility.GetActiveDamageBonus(flow);

            if (bonus <= FP._0)
                return;

            StatusEffectUtility.ApplyTempOutgoingDamage(f, entity, f.DeltaTime * 4, bonus);
        }

        private static void TickCooldowns(Frame f, ZaraFlow* flow)
        {
            if (flow->KeepTheBeatCooldownRemaining > FP._0)
                flow->KeepTheBeatCooldownRemaining -= f.DeltaTime;

            if (flow->HypeCooldownRemaining > FP._0)
                flow->HypeCooldownRemaining -= f.DeltaTime;
        }

        // INTENTIONAL movement only. Reads the player's own input Direction rather than velocity or
        // position delta, which is the entire reason knockback, teleports, physics shoves, conveyor-
        // style displacement and any other external force can never build Flow - none of them touch
        // input, so none of them need their own exclusion check.
        //
        // A Dash counts explicitly: it is Zara-controlled locomotion she chose to spend a resource on,
        // and it is the single most on-theme way to keep the rhythm going. It counts even on a tick
        // where the stick has returned to neutral mid-dash, which a raw input check would miss.
        //
        // The threshold rejects a resting thumb on a drifting analog stick. Compared squared to keep it
        // to one multiply and no square root.
        private static bool ResolveIsMoving(Frame f, EntityRef entity, ZaraFlow* flow)
        {
            if (IsDashing(f, entity) == true)
                return true;

            if (f.Unsafe.TryGetPointer<PlayerLink>(entity, out var playerLink) == false)
                return false;

            FPVector2 direction = PlayerInputUtility.Resolve(f, entity, playerLink)->Direction;

            return direction.SqrMagnitude > flow->MovementInputThreshold * flow->MovementInputThreshold;
        }

        // Read off the Dash slot's own lifecycle rather than off the IgnoreProjectile layer swap that
        // happens to accompany a dash - the layer is an implementation detail of Dash's i-frames, while
        // SkillState.Active is what the skill itself considers "running".
        private static bool IsDashing(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true
                   && skills->DashSkill.State == SkillState.Active;
        }

        // Fills the bar over BuildDuration seconds of movement. Already-full is a no-op rather than a
        // special case - SetProgress ignores a write that doesn't change the value.
        private static void TickBuild(Frame f, EntityRef entity, ZaraFlow* flow)
        {
            FP duration = ResolveBuildDuration(flow);

            if (duration <= FP._0)
                return;

            ZaraFlowUtility.AddProgress(f, entity, flow, f.DeltaTime / duration);
        }

        // Stationary: hold everything for the grace window, then drain the bar over DecayDuration.
        // Progress is deliberately NOT touched during grace - a brief stop to fire or turn must cost
        // nothing at all, which is the whole point of the window existing.
        private static void TickStationary(Frame f, EntityRef entity, ZaraFlow* flow)
        {
            flow->StationaryTimer += f.DeltaTime;

            if (flow->StationaryTimer < flow->StationaryGrace)
                return;

            if (flow->DecayDuration <= FP._0)
                return;

            ZaraFlowUtility.AddProgress(f, entity, flow, -(f.DeltaTime / flow->DecayDuration));
        }

        // Faster Tempo scales the RATE, so the duration is divided rather than overwritten - a rank that
        // says "builds 25% faster" stays true regardless of what the baseline is later retuned to.
        public static FP ResolveBuildDuration(ZaraFlow* flow)
        {
            FP multiplier = flow->BuildRateMultiplier > FP._0 ? flow->BuildRateMultiplier : FP._1;

            return flow->BuildDuration / multiplier;
        }

        // The authoritative "was I hit?" - fires for a hit negated by the Accessory Guard or a Free Hit
        // Guard exactly as it does for one that lands, and never for a hit she dodged or i-framed. See
        // Combat.qtn.
        //
        // Keep the Beat's damage reduction is returned from the utility and applied through the shared
        // reactive-DR slot, which lands on THIS hit because the signal is dispatched synchronously above
        // DamageUtility's own resolution steps. Routing it through that generic slot rather than a
        // bespoke hook is what keeps it from interfering with Accessory durability or Free Hit Guard
        // logic - both of those sit above it and have already had their say by the time DR is read.
        public void OnHostileHitConnected(Frame f, EntityRef target, EntityRef attacker)
        {
            if (f.Unsafe.TryGetPointer<ZaraFlow>(target, out var flow) == false)
                return;

            FP damageMultiplier = ZaraFlowUtility.OnHostileHitConnected(f, target, flow);

            if (damageMultiplier >= FP._1)
                return;

            StatusEffectUtility.ApplyTemporaryDamageReduction(f, target, f.DeltaTime * 2, FP._1 - damageMultiplier);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ZaraFlow* Flow;
        }
    }
}
