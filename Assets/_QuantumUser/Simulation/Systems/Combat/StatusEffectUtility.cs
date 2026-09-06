namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Single entry point for applying/reading status effects - StatusEffectSystem ticks whatever
    // gets applied here, and DamageUtility/PlayerMovementProcessor/EnemySystem/WeaponSystem read the
    // getters below. Every Apply* is a no-op on a target with no StatusEffects component, same as
    // Armor/Shield being optional in DamageUtility.
    public static unsafe class StatusEffectUtility
    {
        // Refresh-only: Remaining always extends to the new duration, but DamagePerTick/Owner/Source
        // only overwrite when the incoming tick is >= the current one - a weak follow-up hit can
        // extend an already-strong burn's timer but never downgrade its tick damage. Once Remaining
        // has actually hit zero there's nothing to compare against, so the next application always
        // sets fresh. tickInterval is EffectConfig.TickInterval - callers already have config
        // resolved (see TryApplyElementalStatus/BurnEffectData), so it's threaded in rather than
        // re-resolved here.
        public static void ApplyBurn(Frame f, EntityRef target, FP duration, FP damagePerTick,
            EntityRef owner, DamageSource source, FP tickInterval)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                damagePerTick *= resistance.BurnDamageMultiplier;
            }

            bool wasActive = status->BurnRemaining > FP._0;

            if (wasActive == false || damagePerTick >= status->BurnDamagePerTick)
            {
                status->BurnDamagePerTick = damagePerTick;
                status->BurnOwner = owner;
                status->BurnSource = source;
            }

            if (wasActive == false)
            {
                status->BurnTickTimer = tickInterval;
            }

            status->BurnRemaining = duration;
            MarkFirstElementApplied(status, ElementType.Fire);

            Log.Debug($"[Status] {target} Burn refreshed to {duration}s at {status->BurnDamagePerTick}/tick (incoming {damagePerTick})");
        }

        // Records the FIRST element to ever apply a baseline status to this entity (StatusEffects.
        // FirstElementApplied) - a no-op once already set, even for a different element (see that
        // field's own comment in StatusEffects.qtn). Called by every Apply* below that lands one of the
        // 4 elemental baselines (Fire->Burn/Ice->Slow/Rock->Intimidate/Lightning->Electrified),
        // regardless of which caller/path actually triggered it (normal roll, guaranteed Burn,
        // perk-infused, ...) - so nothing needs to remember to hook this at every call site. Purely a
        // presentation hook - HitFeedback polls GetFirstElementApplied below directly every frame
        // (alongside the matching IsBurning/IsSlowed/IsElectrified/IsIntimidated) to know both WHICH
        // element to tint restColor with and whether that status is STILL active right now, so no event
        // is needed here - the tint should track live active/inactive state, not just "was ever hit".
        private static void MarkFirstElementApplied(StatusEffects* status, ElementType element)
        {
            if (status->FirstElementApplied != ElementType.Neutral)
                return;

            status->FirstElementApplied = element;
        }

        // View-facing read of StatusEffects.FirstElementApplied - Neutral (the default) if the entity
        // was never hit by any of Fire/Ice/Rock/Lightning, or has no StatusEffects component at all.
        public static ElementType GetFirstElementApplied(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true
                ? status->FirstElementApplied
                : ElementType.Neutral;
        }

        // Plain overwrite-on-reapply - no equivalent "downgrade feels bad" concern for a speed
        // multiplier as there is for Burn's tick damage.
        public static void ApplyIce(Frame f, EntityRef target, FP duration, FP speedMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;

                // Same "multiplier on the REDUCTION, not on the raw value" convention SlowEffectData's
                // own magnitudeMultiplier already uses - a Boss's ChillForceMultiplier of 0.4 keeps
                // 40% of however strong this particular application was (whatever speedMultiplier
                // already is by this point, diluted or not), rather than resetting to a fixed value
                // and losing e.g. Zara's Remix strengthening on top.
                speedMultiplier = FP._1 - (FP._1 - speedMultiplier) * resistance.ChillForceMultiplier;
            }

            status->IceRemaining = duration;
            status->IceSpeedMultiplier = speedMultiplier;
            MarkFirstElementApplied(status, ElementType.Ice);
        }

        // owner defaults to None for any caller that genuinely has no attacker to attribute this to
        // (there isn't one today, but every call site should still prefer passing its own real owner
        // over leaving this default).
        //
        // Returns whether the Stun actually landed, so a caller that needs to know (a per-pulse
        // effect wanting to skip its own VFX on a rejected proc) doesn't need a second query. Every
        // pre-existing call site ignores it, unchanged.
        //
        // Hard-CC diminishing returns: an enemy tier authored with a StunImmunityDuration (or with
        // ImmuneToHardCC, i.e. Boss) rejects a Stun outright while its own window is still running -
        // see StatusEffects.StunImmunityRemaining. Both default to 0/false, so a target with no tier
        // resistance at all (the player, any non-Enemy) keeps the original plain
        // overwrite-on-reapply behavior exactly.
        public static bool ApplyStun(Frame f, EntityRef target, FP duration, EntityRef owner = default)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return false;

            FP immunityWindow = FP._0;

            if (GetTierResistance(f, target) is { } resistance)
            {
                if (resistance.ImmuneToHardCC == true)
                    return false;

                duration *= resistance.StunDurationMultiplier;
                immunityWindow = resistance.StunImmunityDuration;
            }

            if (immunityWindow > FP._0 && status->StunImmunityRemaining > FP._0)
                return false;

            status->StunRemaining = duration;

            if (immunityWindow > FP._0)
            {
                status->StunImmunityRemaining = duration + immunityWindow;
            }

            return true;
        }

        // Interrupt half of the same generic hard-CC diminishing-returns mechanism ApplyStun uses -
        // an action-interrupt isn't a status effect of its own (nothing lingers on the target), so
        // EnemyActionUtility.TryInterrupt calls this as a pure check-then-consume gate rather than
        // going through an Apply* method. Returns false when the interrupt should be rejected.
        // Anything with no tier resistance, or a tier authored with InterruptImmunityDuration 0
        // (Filler/Normal), is never gated.
        public static bool TryConsumeInterruptImmunity(Frame f, EntityRef target)
        {
            if (GetTierResistance(f, target) is not { } resistance)
                return true;

            if (resistance.ImmuneToHardCC == true)
                return false;

            if (resistance.InterruptImmunityDuration <= FP._0)
                return true;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return true; // nowhere to record the window - don't silently block the interrupt

            if (status->InterruptImmunityRemaining > FP._0)
                return false;

            status->InterruptImmunityRemaining = resistance.InterruptImmunityDuration;
            return true;
        }

        // Unlike Stun, only pins movement - PlayerMovementProcessor/EnemySystem read this
        // separately from IsStunned so attacking/skills/firing stay untouched.
        //
        // Fires the generic EntityRooted view event right here so every Root source gets VFX for
        // free, instead of each caller (JuggernautLandingImpactSystem today, others later) deriving
        // its own position/radius and firing a bespoke event - see EntityRooted in Events.qtn.
        public static void ApplyRoot(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            // Root is hard CC, so it obeys the same per-tier immunity Stun does.
            if (GetTierResistance(f, target) is { } resistance)
            {
                if (resistance.ImmuneToHardCC == true)
                    return;

                duration *= resistance.RootDurationMultiplier;
            }

            status->RootRemaining = duration;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == true)
            {
                FP radius = f.Unsafe.TryGetPointer<PhysicsCollider3D>(target, out var collider) == true
                    ? EnemyMovementUtility.ResolveShapeRadius(collider->Shape)
                    : FP._0;

                f.Events.EntityRooted(target, transform->Position, radius);
            }
        }

        // Take-the-stronger/longer semantics on reapply, same shape as
        // ApplyTemporaryDamageReduction/ApplyTemporaryWeaponDamage below: a weaker/shorter Rupture
        // landing while a stronger one is still active extends nothing and overwrites nothing, so
        // it can never cut the stronger window short. Added once Scrapjaw became the first entity
        // with multiple independent Rupture sources (wall-hit charge, Scrapstorm finish, combo-chain
        // finish) that could plausibly land close together.
        public static void ApplyRupture(Frame f, EntityRef target, FP duration, FP damageMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.RuptureDurationMultiplier;
            }

            bool active = status->RuptureRemaining > FP._0;

            if (active == false || damageMultiplier >= status->RuptureDamageMultiplier)
            {
                status->RuptureDamageMultiplier = damageMultiplier;
                status->RuptureRemaining = duration;
            }
            else if (duration > status->RuptureRemaining)
            {
                status->RuptureRemaining = duration;
            }
        }

        // Enemy-only - a target with no Enemy component (the player) has no tier to resist with,
        // so every Apply*/ResolveKnockbackScale caller treats a null return as "unresisted".
        // Internal (not private) so DamageUtility.ResolveKnockbackScale can fold in
        // KnockbackMultiplier alongside its own CharacterStats-based scale.
        internal static TierStatusResistance GetTierResistance(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return null;

            EnemyTierResistanceConfig config = f.FindAsset(f.RuntimeConfig.EnemyTierResistanceConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.EnemyTierResistanceConfig - is it assigned in the RuntimeConfig asset?");
                return null;
            }

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return config.Get(data.Tier);
        }

        public static FP GetSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->IceRemaining <= FP._0)
                return FP._1;

            return status->IceSpeedMultiplier;
        }

        // Plain overwrite-on-reapply, same as Ice - no "downgrade feels bad" concern for a slow
        // multiplier the way Burn's tick damage has. Folds in the same SlowDurationMultiplier tier
        // resistance Ice already uses (a time-dilation debuff is a slow archetype effect), rather than
        // adding a dedicated resistance field for just this one status.
        public static void ApplyTimeDilation(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;
            }

            status->TimeDilationRemaining = duration;
            status->TimeDilationMultiplier = multiplier;
        }

        // Read only at each EnemyDeliveryData's own Active-phase timer decrement (see
        // LeapDeliveryData/ChargeDeliveryData/BeamDeliveryData/AuraDeliveryData/PullGrabDeliveryData) -
        // never the Preparation/Recovery timers in EnemySystem itself, and never a cooldown, so this
        // only ever slows an attack while it's actually executing.
        public static FP GetLocalTimeMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TimeDilationRemaining <= FP._0)
                return FP._1;

            return status->TimeDilationMultiplier;
        }

        // Applied by FreezeEffectData, a standalone freely-authorable skill effect - mirrors
        // ApplyTimeDilation's shape exactly, but targets the opposite phase (see
        // GetAnticipationMultiplier below). Plain overwrite-on-reapply, same resistance fold-in as
        // TimeDilation/Ice since it's the same slow archetype.
        public static void ApplyAnticipationSlow(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;
            }

            status->AnticipationSlowRemaining = duration;
            status->AnticipationSlowMultiplier = multiplier;
        }

        // Read only at EnemySystem.UpdatePreparation's own StateTimer decrement - the
        // Preparation/Telegraph windup, never the Active-phase timer GetLocalTimeMultiplier already
        // owns (see that field's own comment on why TimeDilation is explicitly excluded from this
        // phase). A value < 1 stretches the windup, giving a longer read/dodge window without
        // stopping the attack outright.
        public static FP GetAnticipationMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->AnticipationSlowRemaining <= FP._0)
                return FP._1;

            return status->AnticipationSlowMultiplier;
        }

        // Read by view-only code (HUD indicator, particle VFX) to tell whether Freeze is currently
        // active, without needing the multiplier itself - same shape as IsSlowed/IsStunned above.
        public static bool IsAnticipationSlowed(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->AnticipationSlowRemaining > FP._0;
        }

        // Plain overwrite-on-reapply, same as Ice/Rupture - no "downgrade feels bad" concern for a
        // damage-reduction fraction the way Burn's tick damage has.
        public static void ApplyDamageReduction(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->DamageReductionRemaining = duration;
            status->DamageReductionAmount = amount;
        }

        // Returns a multiplier (1 = no reduction), same convention as GetIncomingDamageMultiplier -
        // folded into DamageUtility.ResolveDamageReduction alongside CharacterStats.DamageReduction's
        // own permanent fraction, the two stacking rather than one replacing the other.
        public static FP GetDamageReductionMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->DamageReductionRemaining <= FP._0)
                return FP._1;

            return FPMath.Clamp(FP._1 - status->DamageReductionAmount, FP._0, FP._1);
        }

        // The shared CONTINUOUS-AURA damage-reduction slot - Brute's Guardian and Lux's Fire Support
        // both write here, deliberately sharing ONE slot rather than each owning their own pair. That
        // sharing IS the stacking policy: two aura sources never stack additively, the strongest wins
        // (take-the-stronger/longer on reapply, so a weaker aura refreshing every tick can never cut a
        // stronger one's window short, and neither can it downgrade it). Kept separate from the
        // generic ApplyDamageReduction pair so a per-tick aura refresh can't stomp a rarer reactive
        // proc - see StatusEffects.qtn's own comment on the field pair.
        public static void ApplyAuraDamageReduction(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            bool active = status->AuraDamageReductionRemaining > FP._0;

            if (active == false || amount >= status->AuraDamageReductionAmount)
            {
                status->AuraDamageReductionAmount = amount;
                status->AuraDamageReductionRemaining = duration;
            }
            else if (duration > status->AuraDamageReductionRemaining)
            {
                status->AuraDamageReductionRemaining = duration;
            }
        }

        public static FP GetAuraDamageReductionMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->AuraDamageReductionRemaining <= FP._0)
                return FP._1;

            return FPMath.Clamp(FP._1 - status->AuraDamageReductionAmount, FP._0, FP._1);
        }

        // Second, independent timed DR pair - see StatusEffects.qtn's own comment on
        // TemporaryDamageReductionRemaining/Amount for why this can't share the Guardian pair above.
        // Take-the-stronger/longer semantics on reapply (unlike the plain overwrite every other timed
        // multiplier here uses): a weaker/shorter proc landing while a stronger one is still active
        // extends nothing and overwrites nothing, so it can never cut the stronger window short.
        public static void ApplyTemporaryDamageReduction(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            bool active = status->TemporaryDamageReductionRemaining > FP._0;

            if (active == false || amount >= status->TemporaryDamageReductionAmount)
            {
                status->TemporaryDamageReductionAmount = amount;
                status->TemporaryDamageReductionRemaining = duration;
            }
            else if (duration > status->TemporaryDamageReductionRemaining)
            {
                status->TemporaryDamageReductionRemaining = duration;
            }
        }

        public static FP GetTemporaryDamageReductionMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TemporaryDamageReductionRemaining <= FP._0)
                return FP._1;

            return FPMath.Clamp(FP._1 - status->TemporaryDamageReductionAmount, FP._0, FP._1);
        }

        // FREE HIT GUARD - a one-shot, timed, complete negation of the next damaging hit. Generic and
        // hero-agnostic (Brute's Bodyguard is simply the first consumer); see StatusEffects.qtn.
        //
        // Take-the-longer on reapply, matching every other timed slot here. There is no "magnitude" to
        // compare - a free hit is a free hit - so the only question a second grant can answer is
        // whether it lasts longer than what's already running. source is overwritten alongside a
        // longer window, so the granter who actually ends up saving someone is the one paid back.
        public static void ApplyFreeHitGuard(Frame f, EntityRef target, EntityRef source, FP duration)
        {
            if (duration <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (duration <= status->FreeHitGuardRemaining)
                return;

            status->FreeHitGuardRemaining = duration;
            status->FreeHitGuardDuration = duration;
            status->FreeHitGuardSource = source;
        }

        public static bool HasFreeHitGuard(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true
                   && status->FreeHitGuardRemaining > FP._0;
        }

        // Who granted the guard currently running, or EntityRef.None if there isn't one. Non-consuming
        // - the read a refreshing source needs to answer "is the guard standing on this ally MINE?",
        // so it can keep its own alive without also extending someone else's indefinitely.
        public static EntityRef GetFreeHitGuardSource(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false
                || status->FreeHitGuardRemaining <= FP._0)
                return EntityRef.None;

            return status->FreeHitGuardSource;
        }

        // Spends the guard if one is up, reporting who granted it so the caller can raise
        // OnFreeHitGuardConsumed. Clears the window outright rather than letting it tick out - it's
        // one hit, not a duration of immunity, so a second hit the same tick must land normally.
        public static bool TryConsumeFreeHitGuard(Frame f, EntityRef target, out EntityRef source)
        {
            source = EntityRef.None;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false
                || status->FreeHitGuardRemaining <= FP._0)
                return false;

            source = status->FreeHitGuardSource;

            status->FreeHitGuardRemaining = FP._0;
            status->FreeHitGuardDuration = FP._0;
            status->FreeHitGuardSource = EntityRef.None;

            return true;
        }

        // Temporary Weapon Damage buff (Max's Last Stand rank 2 retaliation proc, Run & Gun rank 2) -
        // take-the-stronger/longer semantics on reapply, same shape as ApplyTemporaryDamageReduction:
        // a weaker/shorter proc landing while a stronger one is still active extends nothing and
        // overwrites nothing, so it can never cut the stronger window short.
        public static void ApplyTemporaryWeaponDamage(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            bool active = status->TemporaryWeaponDamageRemaining > FP._0;

            if (active == false || amount >= status->TemporaryWeaponDamageAmount)
            {
                status->TemporaryWeaponDamageAmount = amount;
                status->TemporaryWeaponDamageRemaining = duration;
            }
            else if (duration > status->TemporaryWeaponDamageRemaining)
            {
                status->TemporaryWeaponDamageRemaining = duration;
            }
        }

        // Read by DamageUtility.ResolveOutgoingDamage's existing DamageSource.Weapon block - replaces
        // the old dead-Adrenaline-system GetWeaponDamageMultiplier call there.
        public static FP GetTemporaryWeaponDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TemporaryWeaponDamageRemaining <= FP._0)
                return FP._1;

            return FP._1 + status->TemporaryWeaponDamageAmount;
        }

        // Generic timed OUTGOING-damage buff, applying to EVERY DamageSource (unlike
        // ApplyTemporaryWeaponDamage just above, which is scoped to DamageSource.Weapon) - a
        // tempo-support buff (Zara's Power Chord) is meant to speed up whatever the buffed ally's own
        // build actually does, not just their gun. Take-the-stronger/longer on reapply, same shape,
        // so a source pulsing every beat can never cut a stronger window short.
        public static void ApplyTempOutgoingDamage(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            bool active = status->TempOutgoingDamageRemaining > FP._0;

            if (active == false || amount >= status->TempOutgoingDamageAmount)
            {
                status->TempOutgoingDamageAmount = amount;
                status->TempOutgoingDamageRemaining = duration;
            }
            else if (duration > status->TempOutgoingDamageRemaining)
            {
                status->TempOutgoingDamageRemaining = duration;
            }
        }

        public static FP GetTempOutgoingDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TempOutgoingDamageRemaining <= FP._0)
                return FP._1;

            return FP._1 + status->TempOutgoingDamageAmount;
        }

        // Run & Gun rank 3 - checked directly by WeaponSystem right before its own unconditional
        // Weapon.Ammo--, same "read by name" idiom IsStunned/IsRooted use for a plain live-window
        // check with no multiplier to return.
        public static bool HasNoAmmoConsumption(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->NoAmmoConsumptionRemaining > FP._0;
        }

        // Plain overwrite-on-reapply, same as Rupture - no "downgrade feels bad" concern for a debuff
        // multiplier the way Burn's tick damage has.
        public static void ApplyIntimidate(Frame f, EntityRef target, FP duration, FP damageMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->IntimidateRemaining = duration;
            status->IntimidateDamageMultiplier = damageMultiplier;
            MarkFirstElementApplied(status, ElementType.Rock);
        }

        // Read at DamageUtility.ResolveOutgoingDamage, BEFORE that method's CharacterStats gate -
        // this has to reduce an enemy's own outgoing damage, and enemies never carry CharacterStats.
        public static FP GetOutgoingDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->IntimidateRemaining <= FP._0)
                return FP._1;

            return status->IntimidateDamageMultiplier;
        }

        public static bool IsIntimidated(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->IntimidateRemaining > FP._0;
        }

        // Generic (not Kai-named) - marks a target as Bound, a plain flag other effects can key bonus
        // damage off (Kai's Undertow rank 3 "Gravitational Bond" is the first consumer). Plain
        // overwrite-on-reapply, same as Ice/Stun/Root.
        public static void ApplyBound(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->BoundRemaining = duration;
        }

        public static bool IsBound(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->BoundRemaining > FP._0;
        }

        // Plain overwrite-on-reapply, same as every other timed multiplier here.
        public static void ApplyKnockbackTaken(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->KnockbackTakenRemaining = duration;
            status->KnockbackTakenMultiplier = multiplier;
        }

        // Folded into DamageUtility.ResolveKnockbackScale alongside CharacterStats.
        // KnockbackTakenMultiplier and the enemy-tier resistance multiplier.
        public static FP GetKnockbackTakenMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->KnockbackTakenRemaining <= FP._0)
                return FP._1;

            return status->KnockbackTakenMultiplier;
        }

        // Stacks by source - same source re-applying (e.g. a continuous aura re-triggering every
        // tick it's in range) refreshes its own slot in place instead of consuming a new one, so it
        // can't crowd out a genuinely different source. See StatusEffects.qtn.
        public static void ApplyHaste(Frame f, EntityRef target, EntityRef source, FP duration, FP attackSpeedMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
            {
                Log.Debug($"[Status] {target} has no StatusEffects component - Haste skipped");
                return;
            }

            int slot = FindHasteSlot(status, source, out bool evicted);

            status->HasteRemaining[slot] = duration;
            status->HasteAttackSpeedMultiplier[slot] = attackSpeedMultiplier;
            status->HasteSource[slot] = source;

            if (evicted == true)
            {
                Log.Debug($"[Status] {target} Haste stacks full - evicted soonest-expiring slot {slot} for {source}'s {duration}s at x{attackSpeedMultiplier}");
            }
            else
            {
                Log.Debug($"[Status] {target} Haste from {source} applied/refreshed - {duration}s at x{attackSpeedMultiplier}");
            }
        }

        private static int FindHasteSlot(StatusEffects* status, EntityRef source, out bool evicted)
        {
            evicted = false;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0 && status->HasteSource[i] == source)
                    return i;
            }

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] <= FP._0)
                    return i;
            }

            int soonestToExpire = 0;

            for (int i = 1; i < 4; i++)
            {
                if (status->HasteRemaining[i] < status->HasteRemaining[soonestToExpire])
                    soonestToExpire = i;
            }

            evicted = true;
            return soonestToExpire;
        }

        // Read by StatUtility.GetFireCooldown alongside CharacterStats.AttackSpeedMultiplier -
        // independent of it, since StatusEffects and CharacterStats are separate components
        // (an entity could in principle have one without the other). Multiplies every active slot
        // together so distinct Haste sources stack instead of one overwriting another.
        public static FP GetAttackSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false)
                return FP._1;

            FP multiplier = FP._1;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0)
                    multiplier *= status->HasteAttackSpeedMultiplier[i];
            }

            return multiplier;
        }

        // Read by view-only code (e.g. a buff glow) to tell whether Haste is currently active,
        // without needing the multiplier itself - same shape as HasShieldRegenBuff below.
        public static bool HasHasteBuff(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0)
                    return true;
            }

            return false;
        }

        // Plain overwrite-on-reapply, same as Haste - a buff has no "downgrade feels bad" concern the
        // way Burn's tick damage does. No GetTierResistance fold-in - that's an enemy-only debuff
        // concept (see its own comment), and this is a buff applied to allies.
        public static void ApplyShieldRegen(Frame f, EntityRef target, FP duration, FP shieldRegenMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
            {
                Log.Debug($"[Status] {target} has no StatusEffects component - ShieldRegen skipped");
                return;
            }

            status->ShieldRegenRemaining = duration;
            status->ShieldRegenMultiplier = shieldRegenMultiplier;

            Log.Debug($"[Status] {target} ShieldRegen applied - {duration}s at x{shieldRegenMultiplier} recharge rate");
        }

        // Read by ShieldSystem alongside its own RechargeRate.
        public static FP GetShieldRegenMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->ShieldRegenRemaining <= FP._0)
                return FP._1;

            return status->ShieldRegenMultiplier;
        }

        public static FP GetIncomingDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->RuptureRemaining <= FP._0)
                return FP._1;

            return status->RuptureDamageMultiplier;
        }

        public static bool IsStunned(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->StunRemaining > FP._0;
        }

        public static bool IsRooted(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->RootRemaining > FP._0;
        }

        // Read by ShieldSystem to skip waiting out Shield.RechargeTimer entirely while buffed - a
        // sentry's Shield Area Rate aura should let an ally start recharging immediately instead of
        // still having to go untouched for RechargeDelay first, otherwise the faster rate
        // GetShieldRegenMultiplier grants never gets a chance to matter in a sustained fight.
        public static bool HasShieldRegenBuff(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->ShieldRegenRemaining > FP._0;
        }

        // Read by view-only code (e.g. EnemyStatusEffectsView) to toggle a per-status VFX - same
        // shape as IsStunned/IsRooted/HasHasteBuff above.
        public static bool IsBurning(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->BurnRemaining > FP._0;
        }

        public static bool IsSlowed(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->IceRemaining > FP._0;
        }

        public static bool HasRuptureDebuff(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->RuptureRemaining > FP._0;
        }

        // Shared "X% of the triggering hit, spread across tickInterval-spaced ticks over duration"
        // formula - used by BurnEffectData and TryApplyElementalStatus's Fire case so those callers
        // don't each re-derive it. tickInterval is always EffectConfig.TickInterval - threaded in
        // rather than read off a static, so this stays a pure function of its arguments.
        public static FP ComputeDotDamagePerTick(FP hitDamage, FP damagePercent, FP duration, FP tickInterval)
        {
            FP ticks = duration / tickInterval;
            return hitDamage * damagePercent / ticks;
        }

        // Same formula as above, but never lets the result fall below floorPercent of the OWNER's
        // own MaxHealth, spread across ticks the exact same way - a genuine minimum, not just a
        // zero-damage fallback. Covers both the 0-damage utility-proc case (a knockback-only proc, a
        // heal pulse) AND a real but small hit whose 10%/5% share would otherwise be negligible (e.g.
        // a 2-damage tick's own 10% Burn share is 0.2 total, but a 100-HP owner's 5% floor is 5 -
        // the floor wins). A hit whose own share already clears the floor is unaffected.
        public static FP ComputeDotDamagePerTickWithFloor(Frame f, EntityRef owner, FP hitDamage,
            FP damagePercent, FP floorPercent, FP duration, FP tickInterval)
        {
            FP damagePerTick = ComputeDotDamagePerTick(hitDamage, damagePercent, duration, tickInterval);

            FP ownerMaxHealth = f.Unsafe.TryGetPointer<Health>(owner, out var health) == true ? health->MaxHealth : FP._0;
            FP floorPerTick = ComputeDotDamagePerTick(ownerMaxHealth, floorPercent, duration, tickInterval);

            return FPMath.Max(damagePerTick, floorPerTick);
        }

        // Single resolve point for RuntimeConfig.EffectConfig - every EffectData class and both
        // elemental-proc helpers below go through this instead of each repeating the null/log check,
        // same shape as GetTierResistance above.
        public static EffectConfig GetEffectConfig(Frame f)
        {
            EffectConfig config = f.FindAsset(f.RuntimeConfig.EffectConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.EffectConfig - is it assigned in the RuntimeConfig asset?");
            }

            return config;
        }

        // Single resolve point for RuntimeConfig.ElementalReactionConfig - same shape as
        // GetEffectConfig above.
        public static ElementalReactionConfig GetElementalReactionConfig(Frame f)
        {
            ElementalReactionConfig config = f.FindAsset(f.RuntimeConfig.ElementalReactionConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.ElementalReactionConfig - is it assigned in the RuntimeConfig asset?");
            }

            return config;
        }

        // Fire->Burn/Ice->Slow/Rock->Intimidate/Lightning->Electrified/Void->nothing mapping, gated by
        // the owner's CharacterStats.ElementalChance (same roll crit uses) - unlike the old
        // ElementalProcEffectData there's no separate authorable asset, just this one function.
        // Applies whenever a Weapon-sourced hit carries a non-Neutral element and the roll succeeds;
        // called directly from WeaponSystem.FireHitscan (hitscan has no Effects list to run this
        // through) and from inside HitEffectUtility.ApplyToTarget, which covers both Projectile hits
        // and AreaDamage hits (e.g. a grenade's blast) since both funnel through it already.
        //
        // After the landing element's own baseline is applied, TryTriggerElementalReaction checks
        // whether the OTHER status of a reaction pair (Burn/Chill/Electrified) is already live on the
        // target and fires the one matching reaction immediately - see docs/elemental-reactions.md.
        public static void TryApplyElementalStatus(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage)
        {
            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            if (TryApplyGuaranteedBurn(f, target, owner, source, hitDamage, config))
                TryTriggerElementalReaction(f, target, owner, source, ElementType.Fire, hitDamage);

            if (source != DamageSource.Weapon || element == ElementType.Neutral || target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (DamageUtility.RollChance(f, stats->ElementalChance) == false)
                return;

            ApplyElementBaseline(f, target, owner, source, element, hitDamage, config);

            TryTriggerElementalReaction(f, target, owner, source, element, hitDamage);

            Log.Debug($"[Status] {owner}'s {element} weapon hit applied its status to {target}");
        }

        // The EXTRA element grafted on by an Element Infusion weapon perk (WeaponElementInfusion) -
        // same baseline + reaction check as TryApplyElementalStatus, but rolled against the perk's
        // own procChance rather than the owner's CharacterStats.ElementalChance, and with no
        // guaranteed-burn pass (that's owner-global and already ran on this hit's base-element call -
        // running it twice would double it). Called right after the base-element application from
        // HitEffectUtility.ApplyToTarget (projectile/area hits) and WeaponSystem.FireHitscan
        // (hitscan). No-ops for a Neutral element, so a weapon without the perk pays nothing.
        public static void TryApplyInfusedElement(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP procChance, FP hitDamage)
        {
            if (source != DamageSource.Weapon || element == ElementType.Neutral || target == EntityRef.None)
                return;

            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            if (DamageUtility.RollChance(f, procChance) == false)
                return;

            ApplyElementBaseline(f, target, owner, source, element, hitDamage, config);

            TryTriggerElementalReaction(f, target, owner, source, element, hitDamage);

            Log.Debug($"[Status] {owner}'s infused {element} hit applied its status to {target}");
        }

        // The Fire->Burn/Ice->Slow/Rock->Intimidate/Lightning->Electrified landing baseline, shared by
        // both the native-element (TryApplyElementalStatus) and perk-infused (TryApplyInfusedElement)
        // paths so the mapping lives in one place. Void has no baseline - its identity is a
        // hand-authored WeaponDataAsset trait, not status code (see ElementType.qtn).
        private static void ApplyElementBaseline(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage, EffectConfig config)
        {
            switch (element)
            {
                case ElementType.Fire:
                    ApplyBurn(f, target, config.BurnDuration,
                        ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval),
                        owner, source, config.TickInterval);
                    break;

                case ElementType.Ice:
                    ApplyIce(f, target, config.SlowDuration, config.SlowSpeedMultiplier);
                    break;

                case ElementType.Rock:
                    ApplyIntimidate(f, target, config.IntimidateDuration, config.IntimidateOutgoingDamageMultiplier);
                    break;

                case ElementType.Lightning:
                {
                    ElementalReactionConfig reactionConfig = GetElementalReactionConfig(f);

                    if (reactionConfig != null)
                        ApplyElectrified(f, target, reactionConfig.ElectrifiedDuration);

                    break;
                }

                // Void: no baseline - the caller's reaction-check still runs.
            }
        }

        // Fires immediately, order-independently, the instant a NEW external elemental application
        // lands on a target that already carries the complementary status - each element's own
        // ApplyElementBaseline call above always runs before this, so whichever element lands second
        // naturally observes the other's status already active. No pre-hit snapshot needed (unlike the
        // old Rift Mark affinity-proc check) since these are live "is it currently active" reads, not
        // a consumable stack - and unlike the old Rift Mark model, landing here does NOT consume
        // either status: Burn/Chill/Electrified are persistent conditions a reaction capitalizes on,
        // not a resource it spends, so a sustained elemental build keeps producing reactions as new
        // hits land and each reaction's own cooldown clears, without rebuilding from zero every time.
        // Only a genuinely NEW application can reach this method at all (see this method's 2 call
        // sites in TryApplyElementalStatus/TryApplyInfusedElement, plus BurnEffectData/SlowEffectData
        // below) - Burn's own DoT tick and Electrified's own Jolt call DamageUtility/ApplyStagger
        // directly and never route back through here, so neither can loop back into retriggering a
        // reaction on its own. See docs/elemental-reactions.md.
        //
        // Deterministic priority, never more than one reaction per hit: each case below is an
        // if/else-if, so a hit that could validly feed two reactions (e.g. a Fire hit landing on a
        // target that already carries BOTH Chill and Electrified) always resolves to the same one -
        // Thermal Shock > Overload > Shatter, consistently across all three cases. If the
        // higher-priority reaction is still on its own cooldown, nothing falls through to try the
        // next one this same hit; the next NEW hit gets another chance once that cooldown clears.
        //
        // Internal (not private) so BurnEffectData/SlowEffectData (freely-authored, guaranteed-element
        // effects) can call this too for their own element - the weapon-elemental-proc path above
        // isn't the only way Fire/Ice ever lands on a target.
        internal static void TryTriggerElementalReaction(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType newElement, FP hitDamage)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            switch (newElement)
            {
                case ElementType.Fire:
                    if (IsSlowed(f, target) == true)
                        TryTriggerThermalShock(f, status, config, target, owner, source, hitDamage);
                    else if (IsElectrified(f, target) == true)
                        TryTriggerOverload(f, status, config, target, owner, source, hitDamage);
                    break;

                case ElementType.Ice:
                    if (IsBurning(f, target) == true)
                        TryTriggerThermalShock(f, status, config, target, owner, source, hitDamage);
                    else if (IsElectrified(f, target) == true)
                        TryTriggerShatter(f, status, config, target, owner, source);
                    break;

                case ElementType.Lightning:
                    if (IsBurning(f, target) == true)
                        TryTriggerOverload(f, status, config, target, owner, source, hitDamage);
                    else if (IsSlowed(f, target) == true)
                        TryTriggerShatter(f, status, config, target, owner, source);
                    break;
            }
        }

        // Burn + Chill -> Thermal Shock. Single-target burst - deliberately NOT an AoE (unlike the
        // retired Detonation reaction), so it stays useful as a priority-target finisher against
        // Elites/Bosses without also clearing a crowd. Statuses are PERSISTENT CONDITIONS, not
        // consumed by the reaction they enable - Burn/Chill both keep ticking down on their own
        // timers exactly as if this never fired. The reaction's own cooldown (not status removal) is
        // what throttles repeat procs, so a sustained Fire+Ice build can keep triggering Thermal Shock
        // every time a fresh elemental hit lands and the cooldown has cleared, without needing to
        // rebuild both statuses from zero first - see docs/elemental-reactions.md.
        private static bool TryTriggerThermalShock(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner, DamageSource source, FP hitDamage)
        {
            if (status->ThermalShockCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out _) == false)
                return false;

            status->ThermalShockCooldownRemaining = config.ThermalShockTriggerCooldown;

            // A percent of the triggering hit's own damage, same DamagePercent-off-the-triggering-hit
            // convention Overload/Burn/Rupture already use, rather than a flat number disconnected
            // from how hard the hit that actually landed the combo was.
            FP damage = hitDamage * config.ThermalShockDamagePercent;
            DamageUtility.ApplyDamage(f, target, damage, owner, source, bypassOutgoingResolution: true, element: ElementType.Fire, reactionProc: true);

            // ResolveEntityCenter, not raw Transform3D.Position - that's the ground/feet anchor for
            // most enemies, not the visual body center a VFX should spawn at.
            f.Events.ThermalShockTriggered(target, EnemyMovementUtility.ResolveEntityCenter(f, target));

            Log.Debug($"[Status] {target} Burn+Chill triggered Thermal Shock");
            return true;
        }

        // Burn + Shock -> Overload. Sequential chain damage (A->B->C->D, never a fan-out) - deals its
        // own initial hit immediately, then PARKS the chain's continuation state on this same entity's
        // StatusEffects (OverloadChain* fields) instead of resolving every hop synchronously in one
        // frame. StatusEffectSystem.TickOverloadChain/TryAdvanceOverloadChain drive the rest, one hop
        // every OverloadChainDelay real seconds - so a travel-particle "jump" between enemies reads in
        // sync with when the damage actually lands, instead of needing its own disconnected view-side
        // timing. Statuses are PERSISTENT CONDITIONS, not consumed by the reaction - Burn/Electrified
        // both keep ticking down on their own timers, and the cooldown above (not status removal)
        // throttles repeat procs. See docs/elemental-reactions.md.
        private static bool TryTriggerOverload(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner, DamageSource source, FP hitDamage)
        {
            if (status->OverloadCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out _) == false)
                return false;

            status->OverloadCooldownRemaining = config.OverloadTriggerCooldown;

            // A percent of the triggering hit's own damage, same DamagePercent-off-the-triggering-hit
            // convention Burn/Rupture already use - not a flat number disconnected from how hard the
            // hit that actually landed the combo was. This also seeds the chain's own decaying
            // damage pool (OverloadChainCurrentDamage) that TryAdvanceOverloadChain multiplies down
            // hop over hop, rather than each hop dealing a flat, disconnected number.
            FP initialDamage = hitDamage * config.OverloadInitialDamagePercent;
            DamageUtility.ApplyDamage(f, target, initialDamage, owner, source, bypassOutgoingResolution: true, element: ElementType.Lightning, reactionProc: true);

            // ResolveEntityCenter, not raw transform->Position - see TryTriggerThermalShock's own
            // comment. The chain's own OverloadChainPosition uses the same center so every hop's
            // travel-particle segment lines up with each enemy's actual body, not their feet.
            FPVector3 originCenter = EnemyMovementUtility.ResolveEntityCenter(f, target);
            f.Events.OverloadTriggered(target, originCenter);

            status->OverloadChainOwner = owner;
            status->OverloadChainSource = source;
            status->OverloadChainPosition = originCenter;
            status->OverloadChainVisited[0] = target;
            status->OverloadChainVisitedCount = 1;
            status->OverloadChainHopsRemaining = config.OverloadMaxChainTargets;
            status->OverloadChainHopTimer = config.OverloadChainDelay;
            status->OverloadChainCurrentDamage = initialDamage;

            Log.Debug($"[Status] {target} Burn+Shock triggered Overload - chain begins");
            return true;
        }

        // Advances Overload's chain by exactly one hop - called by StatusEffectSystem.TickOverloadChain
        // once per elapsed OverloadChainDelay. `status` is the ORIGIN's own (the chain's logical
        // position is just data on it, OverloadChainPosition/Visited - it never needs to migrate to
        // whichever node the chain currently sits at). Chain damage is raw (bypasses
        // HitEffectUtility/element application entirely) so a chained hit can never itself apply a
        // status or trigger another reaction. Sets HopsRemaining to 0 (the "no chain in progress"
        // live-check) once MaxChainTargets is reached, the visited buffer is full, or no further valid
        // target is found in range - including implicitly if this entity is destroyed mid-chain, since
        // StatusEffectSystem simply stops iterating it.
        internal static void TryAdvanceOverloadChain(Frame f, EntityRef origin, StatusEffects* status)
        {
            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null || status->OverloadChainVisitedCount >= 8)
            {
                status->OverloadChainHopsRemaining = 0;
                return;
            }

            if (TryFindNextChainTarget(f, status, config.OverloadChainRadius, out var nextTarget) == false ||
                f.Unsafe.TryGetPointer<Transform3D>(nextTarget, out _) == false)
            {
                status->OverloadChainHopsRemaining = 0;
                return;
            }

            // A percent of whatever damage the PREVIOUS hop dealt (OverloadChainCurrentDamage), not a
            // flat number and not a percent of the original hit - the chain decays hop over hop. See
            // ElementalReactionConfig.OverloadChainDamagePercent's own comment.
            FP hopDamage = status->OverloadChainCurrentDamage * config.OverloadChainDamagePercent;
            status->OverloadChainCurrentDamage = hopDamage;

            DamageUtility.ApplyDamage(f, nextTarget, hopDamage, status->OverloadChainOwner,
                status->OverloadChainSource, bypassOutgoingResolution: true, element: ElementType.Lightning, reactionProc: true);

            // ResolveEntityCenter, not raw Transform3D.Position - see TryTriggerThermalShock's own
            // comment.
            FPVector3 nextCenter = EnemyMovementUtility.ResolveEntityCenter(f, nextTarget);
            f.Events.OverloadChainLink(origin, nextTarget, status->OverloadChainPosition, nextCenter,
                FPVector3.Distance(status->OverloadChainPosition, nextCenter));

            status->OverloadChainVisited[status->OverloadChainVisitedCount] = nextTarget;
            status->OverloadChainVisitedCount++;
            status->OverloadChainPosition = nextCenter;
            status->OverloadChainHopsRemaining--;

            if (status->OverloadChainHopsRemaining > 0)
                status->OverloadChainHopTimer = config.OverloadChainDelay;
        }

        // Nearest not-yet-visited enemy within radius of the chain's current OverloadChainPosition -
        // adapted from WeaponPerkUtility.TryFindNearestEnemy with an added visited-exclusion list
        // (read off the persisted OverloadChainVisited buffer) and an explicit EntityRef ordinal
        // tie-break for determinism beyond the overlap query's own hit ordering.
        private static bool TryFindNextChainTarget(Frame f, StatusEffects* status, FP radius, out EntityRef result)
        {
            result = EntityRef.None;

            FPVector3 center = status->OverloadChainPosition;
            int visitedCount = status->OverloadChainVisitedCount;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            FP closestSqrDistance = FP.MaxValue;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity == EntityRef.None || f.Unsafe.TryGetPointer<Enemy>(hitEntity, out var enemy) == false)
                    continue;

                // Same Dead/Invulnerable skip WeaponPerkUtility.TryFindNearestEnemy uses - a chain
                // shouldn't hop onto a corpse still mid-death-animation or an untargetable enemy.
                if (enemy->Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(hitEntity) == true)
                    continue;

                bool alreadyVisited = false;

                for (int v = 0; v < visitedCount; v++)
                {
                    if (status->OverloadChainVisited[v] == hitEntity)
                    {
                        alreadyVisited = true;
                        break;
                    }
                }

                if (alreadyVisited == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FP sqrDistance = FPVector3.DistanceSquared(center, hitTransform->Position);

                if (sqrDistance > closestSqrDistance)
                    continue;

                if (sqrDistance == closestSqrDistance && result != EntityRef.None && hitEntity.Index >= result.Index)
                    continue;

                closestSqrDistance = sqrDistance;
                result = hitEntity;
            }

            return result != EntityRef.None;
        }

        // Chill + Shock -> Shatter. AoE CONTROL reaction, deliberately the "radius" geometry alongside
        // Thermal Shock's "point" and Overload's "line/chain" - no pull, no knockback, no new
        // displacement mechanic. The entity that triggered it (the reaction target) becomes the
        // center and never itself moves; it gets a full Stun (the one enemy that actually landed the
        // combo takes the hardest hit), every other valid enemy caught in ShatterRadius gets a SHORT
        // Stagger via StatusEffectUtility.ApplyStagger (tier taper included for free) - the pack
        // around it is interrupted, not fully disabled, only the primary is. Reusing ApplyStun as-is
        // means Boss immunity/tier duration multipliers/the shared Stun diminishing-returns window all
        // apply automatically to the primary with no Shatter-specific special-casing - if the primary
        // happens to be a Boss its own Stun simply won't land, but the reaction still fires and nearby
        // enemies are still staggered. ShatterDamage is optional/0 by default - Shatter's identity is
        // control, not damage. Statuses are PERSISTENT CONDITIONS, not consumed by the reaction -
        // Chill/Electrified both keep ticking down on their own timers, and the cooldown above (not
        // status removal) throttles repeat procs. See docs/elemental-reactions.md.
        private static bool TryTriggerShatter(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner, DamageSource source)
        {
            if (status->ShatterCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out _) == false)
                return false;

            status->ShatterCooldownRemaining = config.ShatterTriggerCooldown;

            ApplyStun(f, target, config.ShatterPrimaryStunDuration, owner);

            if (config.ShatterDamage > FP._0)
                DamageUtility.ApplyDamage(f, target, config.ShatterDamage, owner, source, bypassOutgoingResolution: true, element: ElementType.Ice, reactionProc: true);

            // ResolveEntityCenter, not raw Transform3D.Position - see TryTriggerThermalShock's own
            // comment.
            FPVector3 center = EnemyMovementUtility.ResolveEntityCenter(f, target);

            Shape3D sphere = Shape3D.CreateSphere(config.ShatterRadius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity == EntityRef.None || hitEntity == target || f.Has<Enemy>(hitEntity) == false)
                    continue;

                ApplyStagger(f, hitEntity, config.ShatterAreaStaggerDuration, owner);

                // Same Stagger primitive Shock's own Jolt applies, so it gets the same one-shot spark -
                // ShatterTriggered's own crack VFX only plays once, at the primary/Center, and would
                // otherwise leave every secondary enemy staggered with no visual feedback of its own.
                f.Events.JoltTriggered(hitEntity, EnemyMovementUtility.ResolveEntityCenter(f, hitEntity));

                if (config.ShatterDamage > FP._0)
                    DamageUtility.ApplyDamage(f, hitEntity, config.ShatterDamage, owner, source, bypassOutgoingResolution: true, element: ElementType.Ice, reactionProc: true);
            }

            f.Events.ShatterTriggered(target, center, config.ShatterRadius);

            Log.Debug($"[Status] {target} Chill+Shock triggered Shatter");
            return true;
        }

        // Shock/Electrified - Lightning's baseline (see ApplyElementBaseline). Plain overwrite-on-
        // reapply, no tier duration scaling - modeled on ApplyRoot/IsRooted.
        public static void ApplyElectrified(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->ElectrifiedRemaining = duration;
            MarkFirstElementApplied(status, ElementType.Lightning);
        }

        public static bool IsElectrified(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->ElectrifiedRemaining > FP._0;
        }

        // Stagger - a brief pause of whatever windup timer is currently counting down (see
        // EnemySystem.UpdatePreparation), never a cancel/reset the way Stun is. Modeled on
        // ApplyRoot/IsRooted, but folds in a dedicated StaggerDurationMultiplier tier taper instead of
        // ImmuneToHardCC/an immunity window - Shock needs to stay repeatable/spammable as a periodic
        // interrupt, and Boss should stay interruptible (just tapered), never fully immune.
        public static void ApplyStagger(Frame f, EntityRef target, FP duration, EntityRef owner = default)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.StaggerDurationMultiplier;
            }

            status->StaggerRemaining = duration;
        }

        public static bool IsStaggered(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->StaggerRemaining > FP._0;
        }

        // Independent of the weapon's own Element/ElementalChance roll above - BurnOnHitStacks is a
        // flat guarantee for as long as the granting effect is active, not a proc chance, and fires
        // even on a Neutral weapon. Returns whether it actually applied, so the caller can still fire
        // the reaction scan for this Burn even when the weapon's own Element is Neutral (which
        // otherwise short-circuits before ever reaching TryTriggerElementalReaction).
        private static bool TryApplyGuaranteedBurn(Frame f, EntityRef target, EntityRef owner, DamageSource source, FP hitDamage, EffectConfig config)
        {
            if (source != DamageSource.Weapon || target == EntityRef.None)
                return false;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->BurnOnHitStacks == 0)
                return false;

            ApplyBurn(f, target, config.BurnDuration,
                ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval),
                owner, source, config.TickInterval);

            Log.Debug($"[Status] {owner}'s guaranteed Burn applied to {target}");
            return true;
        }

        // Temporary move-speed buff (Store's Energy Drink food offer, see docs/store-blacksmith.md)
        // - plain overwrite-on-reapply, same as Ice/Haste - a buff has no "downgrade feels bad"
        // concern the way Burn's tick damage does.
        public static void ApplyTempMoveSpeed(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->TempMoveSpeedRemaining = duration;
            status->TempMoveSpeedMultiplier = multiplier;
        }

        // Read by PlayerMovementProcessor alongside CharacterStats.MoveSpeedMultiplier/Ice's own
        // GetSpeedMultiplier - all three compose multiplicatively.
        public static FP GetTempMoveSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TempMoveSpeedRemaining <= FP._0)
                return FP._1;

            return status->TempMoveSpeedMultiplier;
        }

        // Scales a status's duration by the owner's CharacterStats.OutgoingStatusDurationMultiplier,
        // but only for DamageSource.Skill - that field's own doc comment (CharacterStats.qtn) already
        // scopes it to skill-spawned effects, so every status effect asset reuses that exact
        // semantic via this one helper instead of each re-deriving it.
        public static FP ScaleDuration(Frame f, EntityRef owner, DamageSource source, FP duration)
        {
            if (source != DamageSource.Skill)
                return duration;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return duration;

            return duration * stats->OutgoingStatusDurationMultiplier;
        }
    }
}
