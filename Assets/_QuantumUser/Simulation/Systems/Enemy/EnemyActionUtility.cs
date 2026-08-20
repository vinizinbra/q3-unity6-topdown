namespace Quantum
{
    // Generic (hero-agnostic) enemy-action interrupt helper - first consumer is Kai's Singularity
    // Ascension (see docs/kai-ascensions.md), but this has nothing Kai-specific in it. Deliberately
    // independent of EnemyTierStatsConfig.TierStats.CanBeInterruptedByKnockback (the live config has
    // that false for Heavy/Elite/Boss) - that flag governs physical-push resistance, a different
    // concern from a pure state-machine cancel with no impulse behind it. The only pre-existing
    // interrupt path, EnemySystem.OnEnemyKnockedBack, is gated behind that flag AND only ever fires as
    // a side effect of a real knockback landing (see DamageUtility.ApplyResolvedImpulse) - neither fits
    // a caller (like a Vortex's own pull, which deliberately never staggers) that wants to interrupt an
    // enemy's own action without any physics push at all.
    public static unsafe class EnemyActionUtility
    {
        // Covers both halves of an enemy's own action state machine, same Preparation/Telegraph vs.
        // Active branching EnemySystem.OnEnemyKnockedBack already uses - Preparation/Telegraph cancels
        // via CancelWindup (never Active-committed), Active cancels via CancelActive (e.g. a Charger
        // mid-rush or a Leaper mid-air - "already spawned projectiles" from an Execute-phase hit
        // effect are untouched either way, since those are independent entities by the time Active
        // even starts). Never touches Idle/Chasing/Execute/Recovery/Dead. Returns whether an interrupt
        // actually fired, so a caller that needs to know (e.g. a per-cast interruption tracker)
        // doesn't need a second query.
        //
        // ignoreInterruptibleFlag: by default this still respects EnemyActionData.
        // InterruptibleDuringTelegraph/InterruptibleDuringActive (a handful of attacks - e.g.
        // Charger/Grenadier's own charge-up - are explicitly authored non-interruptible on both), the
        // same respectful default any new caller of a generic utility should get. Singularity passes
        // true - it's a dedicated hard-CC Ascension pick, not the passive knockback-interrupt path
        // those flags were originally authored against (see this class's own header comment on why
        // CanBeInterruptedByKnockback is bypassed entirely, same reasoning), so investing in it should
        // punch through both exemptions too.
        public static bool TryInterrupt(Frame f, EntityRef entity, bool ignoreInterruptibleFlag = false)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, enemy->CurrentActionSlot);

            if (action == null)
                return false;

            if (enemy->Phase == EnemyActionPhase.Preparation || enemy->Phase == EnemyActionPhase.Telegraph)
            {
                if (ignoreInterruptibleFlag == false && action.InterruptibleDuringTelegraph == false)
                    return false;

                // Generic hard-CC diminishing returns - consulted AFTER the phase/flag checks so a
                // pulse that wasn't going to interrupt anything anyway never burns the target's
                // immunity window. Filler/Normal (and anything with no tier resistance) are never
                // gated; Boss is rejected outright. See EnemyTierResistanceConfig.
                if (StatusEffectUtility.TryConsumeInterruptImmunity(f, entity) == false)
                    return false;

                EnemySystem.CancelWindup(f, entity, enemy, action);

                Log.Debug($"[Enemy] {entity}'s telegraphed action was interrupted (Phase {enemy->Phase})");
                return true;
            }

            if (enemy->Phase == EnemyActionPhase.Active)
            {
                if (ignoreInterruptibleFlag == false && action.InterruptibleDuringActive == false)
                    return false;

                if (StatusEffectUtility.TryConsumeInterruptImmunity(f, entity) == false)
                    return false;

                EnemySystem.CancelActive(f, entity, enemy, data, action);

                Log.Debug($"[Enemy] {entity}'s active action was interrupted");
                return true;
            }

            return false;
        }
    }
}
