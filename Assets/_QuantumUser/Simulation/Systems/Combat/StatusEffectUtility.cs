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

            Log.Debug($"[Status] {target} Burn refreshed to {duration}s at {status->BurnDamagePerTick}/tick (incoming {damagePerTick})");
        }

        // Rift Mark - stacks 0..config.MaxStacks, clamped. Only ever called from RiftMarkEffectData
        // (a dedicated skill/perk effect), never from the weapon-elemental-proc path - a landing
        // element consumes a mark, it never applies one. All stacks share one duration; reapplying
        // refreshes it (even at max stacks) whenever config.RefreshDurationOnApply is true. duration
        // is threaded in (rather than read off config.BaseDuration directly) so callers can scale it
        // first via ScaleDuration, same shape as every other Apply* here. See docs/elemental-reactions.md.
        public static void ApplyRiftMark(Frame f, EntityRef target, ElementalReactionConfig config, FP duration, byte stacksToAdd)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->RiftMarkStacks = ClampStacks(status->RiftMarkStacks, stacksToAdd, config.MaxStacks);

            if (config.RefreshDurationOnApply == true)
                status->RiftMarkRemaining = duration;

            Log.Debug($"[Status] {target} Rift Mark now at {status->RiftMarkStacks}/{config.MaxStacks} stacks, {status->RiftMarkRemaining}s remaining");
        }

        public static byte GetRiftMarkStacks(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true ? status->RiftMarkStacks : (byte)0;
        }

        public static bool IsRiftMarked(Frame f, EntityRef entity)
        {
            return GetRiftMarkStacks(f, entity) > 0;
        }

        // Consumed only by TryConsumeRiftMarkReaction, once a reaction has actually committed to
        // firing - never independently. Dropping to 0 stacks removes the status outright (also
        // zeroing RiftMarkRemaining) rather than leaving a 0-stack mark to silently expire later.
        private static void ConsumeRiftMarkStack(Frame f, StatusEffects* status, byte stacksToConsume)
        {
            status->RiftMarkStacks = ClampStacks(status->RiftMarkStacks, -stacksToConsume, status->RiftMarkStacks);

            if (status->RiftMarkStacks == 0)
                status->RiftMarkRemaining = FP._0;
        }

        // Pure stack-count math, no Frame/StatusEffects access needed - factored out of
        // ApplyRiftMark/ConsumeRiftMarkStack above so it's covered by plain EditMode tests
        // (Assets/_QuantumUser/Tests/RiftMark) without needing a live Quantum simulation. Clamps to
        // [0, maxStacks] regardless of sign of delta, so the same helper backs both "add stacks up to
        // the cap" and "remove stacks down to zero".
        public static byte ClampStacks(int current, int delta, byte maxStacks)
        {
            int result = current + delta;
            if (result < 0) result = 0;
            if (result > maxStacks) result = maxStacks;
            return (byte)result;
        }

        // Whether preHitStacks/lockout state make this hit a valid Affinity Proc - see
        // TryConsumeRiftMarkReaction's own comment for what "valid" means (a pre-existing mark, and
        // the shared lockout not currently active). Pure so it's covered by EditMode tests
        // independent of TryConsumeRiftMarkReaction's Frame-dependent dispatch/consumption.
        public static bool IsValidAffinityProc(byte preHitStacks, FP reactionLockoutRemaining)
        {
            return preHitStacks > 0 && reactionLockoutRemaining <= FP._0;
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
            }

            status->IceRemaining = duration;
            status->IceSpeedMultiplier = speedMultiplier;
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
        // finish) that could plausibly land close together - every pre-existing single-source caller
        // (the Rock+RiftMark reaction) is unaffected, since it never reapplies mid-window.
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

        // Ice+RiftMark's Deep Freeze reaction - mirrors ApplyTimeDilation's shape exactly, but targets
        // the opposite phase (see GetAnticipationMultiplier below). Plain overwrite-on-reapply, same
        // resistance fold-in as TimeDilation/Ice since it's the same slow archetype.
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

        // Fire->Burn/Ice->Slow/Rock->Intimidate/Lightning->nothing/Void->nothing mapping, gated by the
        // owner's CharacterStats.ElementalChance (same roll crit uses) - unlike the old
        // ElementalProcEffectData there's no separate authorable asset, just this one function.
        // Applies whenever a Weapon-sourced hit carries a non-Neutral element and the roll succeeds;
        // called directly from WeaponSystem.FireHitscan (hitscan has no Effects list to run this
        // through) and from inside HitEffectUtility.ApplyToTarget, which covers both Projectile hits
        // and AreaDamage hits (e.g. a grenade's blast) since both funnel through it already.
        //
        // preHitRiftMarkStacks is captured by the caller (HitEffectUtility.ApplyToTarget/WeaponSystem.
        // FireHitscan) BEFORE this hit's own Effects list runs, so a Rift Mark this same hit applies
        // (via RiftMarkEffectData, later in that Effects list) can never be the one consumed below -
        // see docs/elemental-reactions.md's "event order" section.
        //
        // After the landing element's own baseline is applied (Lightning/Void have none - their
        // identity is hand-authored WeaponDataAsset traits, not status code), TryConsumeRiftMarkReaction
        // checks whether this is a valid Affinity Proc against an existing Rift Mark and fires the one
        // matching reaction - see docs/elemental-reactions.md for the full design.
        public static void TryApplyElementalStatus(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage, byte preHitRiftMarkStacks)
        {
            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            if (TryApplyGuaranteedBurn(f, target, owner, source, hitDamage, config))
                TryConsumeRiftMarkReaction(f, target, owner, source, ElementType.Fire, hitDamage, preHitRiftMarkStacks);

            if (source != DamageSource.Weapon || element == ElementType.Neutral || target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (DamageUtility.RollChance(f, stats->ElementalChance) == false)
                return;

            ApplyElementBaseline(f, target, owner, source, element, hitDamage, config);

            TryConsumeRiftMarkReaction(f, target, owner, source, element, hitDamage, preHitRiftMarkStacks);

            Log.Debug($"[Status] {owner}'s {element} weapon hit applied its status to {target}");
        }

        // The EXTRA element grafted on by an Element Infusion weapon perk (WeaponElementInfusion) -
        // same Fire->Burn/Ice->Slow/Rock->Intimidate baseline + Rift Mark reaction as
        // TryApplyElementalStatus, but rolled against the perk's own procChance rather than the
        // owner's CharacterStats.ElementalChance, and with no guaranteed-burn pass (that's owner-
        // global and already ran on this hit's base-element call - running it twice would double it).
        // Shares the same preHitRiftMarkStacks snapshot the base call captured, so at most one
        // reaction still fires per hit (the base call's, if it landed one - this one's
        // TryConsumeRiftMarkReaction then hits the live reaction lockout it set). Called right after
        // the base-element application from HitEffectUtility.ApplyToTarget (projectile/area hits) and
        // WeaponSystem.FireHitscan (hitscan). No-ops for a Neutral element, so a weapon without the
        // perk pays nothing.
        public static void TryApplyInfusedElement(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP procChance, FP hitDamage, byte preHitRiftMarkStacks)
        {
            if (source != DamageSource.Weapon || element == ElementType.Neutral || target == EntityRef.None)
                return;

            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            if (DamageUtility.RollChance(f, procChance) == false)
                return;

            ApplyElementBaseline(f, target, owner, source, element, hitDamage, config);

            TryConsumeRiftMarkReaction(f, target, owner, source, element, hitDamage, preHitRiftMarkStacks);

            Log.Debug($"[Status] {owner}'s infused {element} hit applied its status to {target}");
        }

        // The Fire->Burn/Ice->Slow/Rock->Intimidate landing baseline, shared by both the native-
        // element (TryApplyElementalStatus) and perk-infused (TryApplyInfusedElement) paths so the
        // mapping lives in one place. Lightning/Void have no baseline - their identity is hand-
        // authored WeaponDataAsset traits, not status code (see ElementType.qtn).
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

                // Lightning/Void: no baseline - the caller's consume-check still runs.
            }
        }

        // A valid Affinity Proc: one of the 5 elements landed on a target that already carried at
        // least one Rift Mark stack BEFORE this hit (preHitRiftMarkStacks, not a live re-read - see
        // TryApplyElementalStatus's own comment), and the shared reaction lockout isn't active.
        // Consumes exactly StacksConsumedPerReaction and fires exactly the one matching reaction -
        // never more than one reaction, and never a bare consume with nothing firing (they're
        // coupled: consumption only happens once a reaction has actually committed to firing, gated
        // by that reaction's own TriggerCooldown same as always). See docs/elemental-reactions.md.
        //
        // Internal (not private) so BurnEffectData/SlowEffectData (freely-authored, guaranteed-element
        // effects) can call this too for their own element - the weapon-elemental-proc path above
        // isn't the only way Fire/Ice ever lands on a target.
        internal static void TryConsumeRiftMarkReaction(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage, byte preHitRiftMarkStacks)
        {
            if (preHitRiftMarkStacks == 0)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (IsValidAffinityProc(preHitRiftMarkStacks, status->RiftMarkReactionLockoutRemaining) == false)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            bool triggered;

            switch (element)
            {
                case ElementType.Fire:
                    triggered = TryTriggerDetonation(f, status, config, target, owner, source, hitDamage);
                    break;
                case ElementType.Ice:
                    triggered = TryTriggerDeepFreeze(f, status, config, target);
                    break;
                case ElementType.Rock:
                    triggered = TryTriggerRupture(f, status, config, target, owner);
                    break;
                case ElementType.Lightning:
                    triggered = TryTriggerOverload(f, status, config, target, owner);
                    break;
                case ElementType.Void:
                    triggered = TryTriggerSingularity(f, status, config, target, owner);
                    break;
                default:
                    triggered = false;
                    break;
            }

            if (triggered == false)
                return;

            ConsumeRiftMarkStack(f, status, config.StacksConsumedPerReaction);
            status->RiftMarkReactionLockoutRemaining = config.ReactionLockoutDuration;
        }

        // Fire + RiftMark -> Detonation. AoE burst, additional to whichever Burn is already active.
        // Damage scales off the triggering hit's own damage, same DamagePercent convention as
        // Burn/Rupture. Fires its own DetonationReleased rather than going through
        // HitEffectUtility.ApplyExplosion's generic WeaponExplosionReleased - this reaction gets a
        // distinct visual (EffectsManager's dedicated detonationEffectPrefab), unlike the weapon-perk
        // explosions that event is shared by.
        private static bool TryTriggerDetonation(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner, DamageSource source, FP hitDamage)
        {
            if (status->DetonationCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return false;

            status->DetonationCooldownRemaining = config.DetonationTriggerCooldown;

            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, config.DetonationRadius, owner,
                hitDamage * config.DetonationDamagePercent, source, DamageTargetMask.Enemies, isExplosion: true);

            f.Events.DetonationReleased(owner, transform->Position, config.DetonationRadius);

            Log.Debug($"[Status] {target} Fire+RiftMark triggered Detonation");
            return true;
        }

        // Ice + RiftMark -> Deep Freeze. Stretches the target's own attack anticipation/windup (see
        // ApplyAnticipationSlow), additional to whichever Slow is already active. Deliberately not a
        // hard lockout - see docs/elemental-reactions.md's "Freeze: stretching anticipation, not
        // stopping the target".
        private static bool TryTriggerDeepFreeze(Frame f, StatusEffects* status, ElementalReactionConfig config, EntityRef target)
        {
            if (status->DeepFreezeCooldownRemaining > FP._0)
                return false;

            status->DeepFreezeCooldownRemaining = config.DeepFreezeTriggerCooldown;
            ApplyAnticipationSlow(f, target, config.DeepFreezeDuration, config.DeepFreezeAnticipationMultiplier);

            Log.Debug($"[Status] {target} Ice+RiftMark triggered Deep Freeze");
            return true;
        }

        // Rock + RiftMark -> Rupture. Increased incoming damage on top of whichever Intimidate is
        // already active, bundled with a knockback impulse (folded in from the old standalone
        // Knockback reaction - see ElementalReactionConfig's own comment). Reuses EffectConfig's own
        // KnockbackTier bucket, same as the old Knockback reaction did.
        private static bool TryTriggerRupture(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner)
        {
            if (status->RuptureCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false ||
                f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false)
                return false;

            EffectConfig effectConfig = GetEffectConfig(f);

            if (effectConfig == null)
                return false;

            status->RuptureCooldownRemaining = config.RuptureTriggerCooldown;
            ApplyRupture(f, target, config.RuptureDuration, config.RuptureDamageTakenMultiplier);

            effectConfig.GetKnockback(KnockbackTier.Strong, out FP force, out FP upwardForce);
            FPVector3 direction = targetTransform->Position - ownerTransform->Position;
            DamageUtility.ApplyKnockback(f, target, direction, force, upwardForce, owner);

            Log.Debug($"[Status] {target} Rock+RiftMark triggered Rupture");
            return true;
        }

        // Lightning + RiftMark -> Overload. Full incapacitation via Stun, own dedicated duration (not
        // EffectConfig.StunDuration, which backs the freely-authorable StunEffectData elsewhere).
        private static bool TryTriggerOverload(Frame f, StatusEffects* status, ElementalReactionConfig config, EntityRef target, EntityRef owner)
        {
            if (status->OverloadCooldownRemaining > FP._0)
                return false;

            status->OverloadCooldownRemaining = config.OverloadTriggerCooldown;
            ApplyStun(f, target, config.OverloadStunDuration, owner);

            Log.Debug($"[Status] {target} Lightning+RiftMark triggered Overload");
            return true;
        }

        // Void + RiftMark -> Singularity. Pulls every enemy within SingularityRadius toward the
        // reaction's own target - a new mechanic, no existing StatusEffects field to reuse. Reuses
        // DamageUtility.ApplyKnockback with the direction inverted (toward target instead of away)
        // for the actual pull, same resistance/scale handling every other push in this system gets.
        private static bool TryTriggerSingularity(Frame f, StatusEffects* status, ElementalReactionConfig config,
            EntityRef target, EntityRef owner)
        {
            if (status->SingularityCooldownRemaining > FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return false;

            status->SingularityCooldownRemaining = config.SingularityTriggerCooldown;

            Shape3D sphere = Shape3D.CreateSphere(config.SingularityRadius);
            var hits = f.Physics3D.OverlapShape(targetTransform->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity == EntityRef.None || hitEntity == target || f.Has<Enemy>(hitEntity) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FPVector3 pullDirection = targetTransform->Position - hitTransform->Position;
                DamageUtility.ApplyKnockback(f, hitEntity, pullDirection, config.SingularityPullForce, FP._0, owner);
            }

            f.Events.SingularityTriggered(owner, targetTransform->Position, config.SingularityRadius);

            Log.Debug($"[Status] {target} Void+RiftMark triggered Singularity");
            return true;
        }

        // Independent of the weapon's own Element/ElementalChance roll above - BurnOnHitStacks is a
        // flat guarantee for as long as the granting effect is active, not a proc chance, and fires
        // even on a Neutral weapon. Returns whether it actually applied, so the caller can still fire
        // the reaction scan for this Burn even when the weapon's own Element is Neutral (which
        // otherwise short-circuits before ever reaching TryConsumeRiftMarkReaction).
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
