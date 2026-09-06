namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Swaps the entity's own AreaDamage.Damage/TargetMask/Effects between AlternatingArea's Heal and
    // Damage configs right before AreaDamageSystem applies them - must run before AreaDamageSystem
    // (see SystemSetup.User). Predicts the same "is a pulse about to fire" condition
    // AreaDamageSystem itself checks a moment later, rather than tracking a second timer -
    // TickInterval/TickTimer stay AreaDamage's alone.
    [Preserve]
    public unsafe class AlternatingAreaSystem : SystemMainThreadFilter<AlternatingAreaSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.AreaDamage->TickTimer > f.DeltaTime)
            {
                TryFireClosingBeat(f, ref filter);
                return;
            }

            bool healNext = filter.Alternating->CurrentlyHealing == false;
            filter.Alternating->CurrentlyHealing = healNext;

            if (healNext == true)
            {
                filter.AreaDamage->Damage = filter.Alternating->HealAmount * ResolveEffectiveness(filter.Alternating);
                filter.AreaDamage->TargetMask = filter.Alternating->HealTargetMask;
                CopyEffects(filter.Alternating->HealEffects, filter.AreaDamage);

                ApplySupportBeatDamage(f, filter.Entity, filter.Alternating);
            }
            else
            {
                filter.AreaDamage->Damage = filter.Alternating->DamageAmount * ResolveEffectiveness(filter.Alternating);
                filter.AreaDamage->TargetMask = filter.Alternating->DamageMask;
                CopyEffects(filter.Alternating->DamageEffects, filter.AreaDamage);

                filter.Alternating->DamagePulseCount++;
                TryApplyBassDropStun(f, filter.Entity, filter.Alternating->DamagePulseCount, filter.AreaDamage);
            }

            f.Events.AlternatingAreaPulsed(filter.Entity, healNext);

            TryFireClosingBeat(f, ref filter);
        }

        // AlternatingArea.DamageEffects (copied in above) already includes anything baked in at
        // spawn time - see SpawnAlternatingAreaEffectData.ApplyAmplifierKnockback. That used to be
        // checked live here instead, off the speaker's owner, but the owning upgrade's Begin/End
        // only brackets the throw itself, which ends before this speaker's later pulses would ever
        // see it - baking it in once at spawn is what actually works for a speaker's whole lifetime.
        // Generic owner-driven scalar (see AlternatingArea.EffectivenessMultiplier). Treats 0 as 1 so an
        // area spawned before this field existed - or by any path that simply never sets it - behaves
        // exactly as it always did rather than silently pulsing for nothing.
        private static FP ResolveEffectiveness(AlternatingArea* alternating)
        {
            return alternating->EffectivenessMultiplier > FP._0 ? alternating->EffectivenessMultiplier : FP._1;
        }

        // Support Beat now also chips enemies for half the Damage Beat's amount, on top of its own
        // heal/buff to allies - the Heal branch above already claims AreaDamage->Damage/TargetMask for
        // this tick (Players), so enemy damage can't ride the same AreaDamage application and instead
        // fires immediately here via a direct HitEffectUtility call, same shape FireBonusPulse uses for
        // an extra beat. Reuses DamageEffects (so a Damage Beat's knockback/on-hit VFX also lands on
        // this half-damage tick) and doesn't touch DamagePulseCount - Bass Drop Stun should still only
        // count genuine Damage Beats, not the Support Beat's bonus damage.
        private static void ApplySupportBeatDamage(Frame f, EntityRef entity, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false
                || f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return;

            AreaOwnerUtility.Resolve(f, entity, out EntityRef owner, out DamageSource source, out ElementType element);

            FP damage = alternating->DamageAmount * FP._0_50 * ResolveEffectiveness(alternating);

            HitEffectUtility.ApplyInCollider(f, alternating->DamageEffects, transform, collider, owner, damage, source,
                null, element, alternating->DamageMask, entity);
        }

        private static void CopyEffects(FixedArray<AssetRef<HitEffectData>> source, AreaDamage* areaDamage)
        {
            for (int i = 0; i < source.Length; i++)
            {
                areaDamage->Effects[i] = source[i];
            }
        }

        // Amplifier rank 3 "Bass Drop" (see Amplifier.qtn/AmplifierSkillAction) - unlike
        // DamageBonus/KnockbackEffect (baked once at spawn, see SpawnAlternatingAreaEffectData), this
        // can't be baked in once at spawn since it should only apply on every StunInterval-th Damage
        // Beat, not every one. Checking it live here, every damage pulse, is safe specifically because
        // AmplifierUpgrade is Begin-only and never revoked - there's no End racing against a live read
        // that never stops happening. Reads off AreaOwner->Owner (the Totem/Speaker's owner), not the
        // pulsing entity itself, same as before this ascension was ranked.
        private static void TryApplyBassDropStun(Frame f, EntityRef speaker, int pulseCount, AreaDamage* areaDamage)
        {
            if (f.Unsafe.TryGetPointer<AreaOwner>(speaker, out var areaOwner) == false)
                return;

            if (f.Unsafe.TryGetPointer<AmplifierUpgrade>(areaOwner->Owner, out var upgrade) == false
                || upgrade->StunInterval == 0 || upgrade->StunEffect.IsValid == false)
                return;

            if (pulseCount % upgrade->StunInterval != 0)
                return;

            for (int i = 0; i < areaDamage->Effects.Length; i++)
            {
                if (areaDamage->Effects[i].IsValid == true)
                    continue;

                areaDamage->Effects[i] = upgrade->StunEffect;

                Log.Debug($"[Skill] {speaker}'s Damage Beat {pulseCount} Bass-Drop-stunned (every {upgrade->StunInterval} beats)");
                return;
            }

            Log.Error($"[Skill] {areaOwner->Owner}'s Bass Drop couldn't fit - {speaker}'s DamageEffects already fills all 4 slots this pulse");
        }

        // Main Stage rank 3 (see MainStage.qtn/MainStageSkillAction) - fires one extra pulse using
        // whichever of AlternatingArea's already-baked Heal/Damage configs isHealing selects, WITHOUT
        // touching AreaDamage.TickTimer/AlternatingArea.CurrentlyHealing - a pure extra beat layered
        // on top of the entity's own normal cadence, never disturbing it. Reuses AreaDamage.Effects as
        // scratch space (via CopyEffects, same as the regular tick above) purely so a bonus Damage Beat
        // still runs through TryApplyBassDropStun and can therefore Stun on its own right, same as any
        // other genuine Damage Beat - the next regular tick fully overwrites AreaDamage.Effects again
        // regardless, so this scratch write never leaks.
        public static void FireBonusPulse(Frame f, EntityRef entity, bool isHealing)
        {
            if (f.Unsafe.TryGetPointer<AlternatingArea>(entity, out var alternating) == false
                || f.Unsafe.TryGetPointer<AreaDamage>(entity, out var areaDamage) == false
                || f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false
                || f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return;

            AreaOwnerUtility.Resolve(f, entity, out EntityRef owner, out DamageSource source, out ElementType element);

            FP damage;
            DamageTargetMask targetMask;

            if (isHealing == true)
            {
                damage = alternating->HealAmount * ResolveEffectiveness(alternating);
                targetMask = alternating->HealTargetMask;
                CopyEffects(alternating->HealEffects, areaDamage);
            }
            else
            {
                damage = alternating->DamageAmount * ResolveEffectiveness(alternating);
                targetMask = alternating->DamageMask;
                CopyEffects(alternating->DamageEffects, areaDamage);

                alternating->DamagePulseCount++;
                TryApplyBassDropStun(f, entity, alternating->DamagePulseCount, areaDamage);
            }

            HitEffectUtility.ApplyInCollider(f, areaDamage->Effects, transform, collider, owner, damage, source,
                null, element, targetMask, entity);

            f.Events.AlternatingAreaPulsed(entity, isHealing);

            Log.Debug($"[Skill] {entity} fired a Main Stage bonus {(isHealing ? "Healing" : "Damage")} Beat");
        }

        // Main Stage rank 3's closing Healing Beat - mirrors VortexSystem.TryExplodeOnDestroy's own
        // "predict destruction one tick early" idiom, so the beat still lands the same tick the Totem/
        // Speaker actually expires, rather than one tick after it's already gone. Guarded on
        // MainStageBonusBeats - a tag stamped only on entities Main Stage itself spawned (see
        // MainStage.qtn's own comment) - so a Portable Speaker, which never carries this tag, can
        // never fire a closing beat regardless of the owner's own Main Stage rank.
        private static void TryFireClosingBeat(Frame f, ref Filter filter)
        {
            if (f.Has<MainStageBonusBeats>(filter.Entity) == false)
                return;

            if (f.Unsafe.TryGetPointer<DestroyAfterTime>(filter.Entity, out var lifetime) == false)
                return;

            if (lifetime->RemainingTime > f.DeltaTime)
                return;

            FireBonusPulse(f, filter.Entity, isHealing: true);

            // Prevents re-entry on a straggling tick (e.g. Duration extended after this already
            // fired) - a closing beat should only ever happen once per Totem/Speaker instance.
            f.Remove<MainStageBonusBeats>(filter.Entity);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public AlternatingArea* Alternating;
            public AreaDamage* AreaDamage;
        }
    }
}
