namespace Quantum
{
    using Photon.Deterministic;

    // Read/write side of Zara's Flow State (see Flow.qtn). Every write to Progress funnels through
    // SetProgress here, which is the single place the Active edge is detected - so the stat bake, the
    // Ascension payoffs and the events can never drift out of step with the bar.
    //
    // Deliberately state-only: no per-tick driving. ZaraFlowSystem owns the clock.
    public static unsafe class ZaraFlowUtility
    {
        // The one place Progress is ever written. Clamps to [0,1], flips IsActive, and fires the
        // activation payoff exactly on the rising edge rather than continuously - which is what makes
        // Headliner's Hype a one-shot on ACTIVATING rather than an aura held while Active.
        public static void SetProgress(Frame f, EntityRef owner, ZaraFlow* flow, FP progress)
        {
            FP clamped = FPMath.Clamp(progress, FP._0, FP._1);

            if (clamped == flow->Progress)
                return;

            bool wasActive = flow->IsActive;

            flow->Progress = clamped;
            flow->IsActive = clamped >= FP._1;

            f.Events.ZaraFlowChanged(owner, clamped, flow->IsActive);

            if (flow->IsActive == wasActive)
                return;

            // Only the TOGGLE costs anything - the bar moving every tick does not, which is most of
            // why collapsing the old 3-stack ladder into one bar made this cheaper as well as simpler.
            ApplyStatBonuses(f, owner, flow);
            RefreshOwnedAreaEffectiveness(f, owner, flow);

            if (flow->IsActive == true)
            {
                TryTriggerHype(f, owner, flow);
            }
        }

        public static void AddProgress(Frame f, EntityRef owner, ZaraFlow* flow, FP amount)
        {
            SetProgress(f, owner, flow, flow->Progress + amount);
        }

        // Rebakes Move Speed / Fire Rate from the SEPARATELY CAPTURED baselines rather than multiplying
        // the live values, so repeated toggles can never compound.
        //
        // Deliberately writes CharacterStats rather than refreshing the shared timed StatusEffects
        // slots every tick: those are take-the-stronger/overwrite, so Flow living there would silently
        // stop Headliner's own Hype buff (which DOES use them) from stacking on top of it.
        public static void ApplyStatBonuses(Frame f, EntityRef owner, ZaraFlow* flow)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            FP moveBonus = FP._0;
            FP fireBonus = FP._0;

            if (flow->IsActive == true)
            {
                moveBonus = flow->MoveSpeedBonus;

                // Faster Tempo rank 3 "Full Tempo" stacks on top of the baseline Active bonus.
                fireBonus = flow->FireRateBonus + flow->ActiveFireRateBonus;
            }

            stats->MoveSpeedMultiplier = flow->BaseMoveSpeedMultiplier * (FP._1 + moveBonus);
            stats->AttackSpeedMultiplier = flow->BaseAttackSpeedMultiplier * (FP._1 + fireBonus);
        }

        // Headliner rank 1 - outgoing damage while Active, refreshed into the generic timed
        // outgoing-damage slot by ZaraFlowSystem.
        //
        // Routed through that shared slot rather than a Zara-specific branch inside
        // DamageUtility.ResolveOutgoingDamage: the generic combat funnel stays hero-agnostic, and the
        // bonus automatically covers every DamageSource she has (weapon, Totem beats, Afterbeats).
        public static FP GetActiveDamageBonus(ZaraFlow* flow)
        {
            return flow->IsActive ? flow->ActiveDamageBonus : FP._0;
        }

        // Owner-only overload for a spawn path with no ZaraFlow pointer to hand. No-ops for every other
        // hero, so a generic spawner can call it unconditionally.
        public static void RefreshOwnedAreaEffectiveness(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<ZaraFlow>(owner, out var flow) == true)
            {
                RefreshOwnedAreaEffectiveness(f, owner, flow);
            }
        }

        // Headliner rank 2 - pushes her current Beat effectiveness onto every AlternatingArea she owns
        // (Totem and Portable Speaker alike). Called on the Active edge and at spawn, so a Totem planted
        // before she activated still picks the bonus up, and one planted while Active correctly loses it
        // when her rhythm breaks.
        //
        // Writes a GENERIC field (AlternatingArea.EffectivenessMultiplier) rather than having
        // AlternatingAreaSystem read ZaraFlow - that system serves any future hero's alternating area
        // and must not know Zara exists.
        public static void RefreshOwnedAreaEffectiveness(Frame f, EntityRef owner, ZaraFlow* flow)
        {
            FP multiplier = flow->IsActive && flow->ActiveBeatEffectiveness > FP._0
                ? FP._1 + flow->ActiveBeatEffectiveness
                : FP._1;

            var areas = f.Filter<AlternatingArea, AreaOwner>();

            while (areas.Next(out EntityRef area, out AlternatingArea _, out AreaOwner areaOwner))
            {
                if (areaOwner.Owner != owner)
                    continue;

                if (f.Unsafe.TryGetPointer<AlternatingArea>(area, out var alternating) == true)
                {
                    alternating->EffectivenessMultiplier = multiplier;
                }
            }
        }

        // A hostile attack connected (see Combat.qtn's OnHostileHitConnected) - her rhythm breaks
        // regardless of whether the hit was ultimately negated. "Guarding saves your health, but not
        // your groove; dodging saves both."
        //
        // Returns the damage multiplier this hit should take, so Keep the Beat's reduction reaches the
        // very hit that triggered it - possible only because Quantum dispatches that signal
        // synchronously, above DamageUtility's own resolution steps.
        public static FP OnHostileHitConnected(Frame f, EntityRef owner, ZaraFlow* flow)
        {
            FP damageMultiplier = FP._1;

            // Keep the Beat (Second Wind rank 3) - checked BEFORE the break, since it is being Active at
            // the moment of the hit that earns the reduction.
            if (flow->KeepTheBeatDamageReduction > FP._0
                && flow->KeepTheBeatCooldownRemaining <= FP._0
                && flow->IsActive == true)
            {
                damageMultiplier = FPMath.Clamp(FP._1 - flow->KeepTheBeatDamageReduction, FP._0, FP._1);
                flow->KeepTheBeatCooldownRemaining = flow->KeepTheBeatCooldown;

                f.Events.ZaraKeepTheBeat(owner);
            }

            // Second Wind rank 1 - a burst of speed to re-establish the rhythm. Granted on the BREAK
            // itself, so it fires whether or not she had a full bar to lose.
            if (flow->SecondWindMoveSpeedBonus > FP._0 && flow->SecondWindDuration > FP._0)
            {
                StatusEffectUtility.ApplyTempMoveSpeed(f, owner, flow->SecondWindDuration,
                    FP._1 + flow->SecondWindMoveSpeedBonus);
            }

            // Baseline wipes the bar; Second Wind rank 2+ leaves a floor to rebuild from. Either way the
            // state switches off - the beat is broken, not merely dented.
            SetProgress(f, owner, flow, flow->ProgressRetainedOnHit);

            flow->StationaryTimer = FP._0;

            f.Events.ZaraFlowBroken(owner, flow->Progress);

            return damageMultiplier;
        }

        // Headliner rank 3 - a one-shot party payoff on ACTIVATION, never an aura held while Active. Its
        // own cooldown stops a Zara who repeatedly bounces off the top of the bar from keeping it up.
        private static void TryTriggerHype(Frame f, EntityRef owner, ZaraFlow* flow)
        {
            if (flow->HypeRadius <= FP._0 || flow->HypeDuration <= FP._0)
                return;

            if (flow->HypeCooldownRemaining > FP._0)
                return;

            flow->HypeCooldownRemaining = flow->HypeCooldown;

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var transform) == false)
                return;

            // Includes dashing allies (and Zara herself mid-dash) - the narrow Player mask cannot see an
            // entity parked on IgnoreProjectile for its dash i-frames, and Flow is a movement mechanic,
            // so a teammate dashing is exactly who this should be catching.
            System.Span<EntityRef> allies = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int alliesCount = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(
                f, transform->Position, flow->HypeRadius, allies);

            for (int i = 0; i < alliesCount; i++)
            {
                EntityRef ally = allies[i];

                if (flow->HypeMoveSpeedBonus > FP._0)
                {
                    StatusEffectUtility.ApplyTempMoveSpeed(f, ally, flow->HypeDuration, FP._1 + flow->HypeMoveSpeedBonus);
                }

                if (flow->HypeFireRateBonus > FP._0)
                {
                    // Owner is Zara, so two of her own triggers share one Haste slot (same source, must
                    // not compound) while a second Zara's Hype gets its own.
                    StatusEffectUtility.ApplyHaste(f, ally, owner, flow->HypeDuration, FP._1 + flow->HypeFireRateBonus);
                }
            }

            f.Events.ZaraHypeTriggered(owner, transform->Position, flow->HypeRadius);
        }
    }
}
