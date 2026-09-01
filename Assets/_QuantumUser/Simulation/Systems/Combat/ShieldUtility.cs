namespace Quantum
{
    using Photon.Deterministic;

    // Shield counterpart to HealUtility - a shared "grant Shield" entry point so every genuine
    // grant (Bodyguard, Lux's Shield Battery aura, ShieldEffectData) fires the same EntityShielded
    // event, driving the same floating-number/particle feedback EntityHealed already gets (see
    // DamageFeedbackManager/EffectsManager) instead of each caller silently writing Shield.Current
    // on its own. Passive recharge (ShieldSystem), initial seeding (CharacterSystem/EnemySystem),
    // and damage absorption (DamageUtility) intentionally do NOT go through here - those aren't a
    // "grant" moment worth celebrating with a number/particle, just book-keeping.
    public static unsafe class ShieldUtility
    {
        // Percent-of-target's-own-Max counterpart to HealUtility.ApplyHeal - same convention (a
        // squishy ally and a tank both get shielded proportionally to their own Max, not the
        // granter's).
        public static void ApplyShield(Frame f, EntityRef target, EntityRef owner, FP shieldPercent)
        {
            if (f.Unsafe.TryGetPointer<Shield>(target, out var shield) == false)
                return;

            ApplyFlatShield(f, target, owner, shield, shield->Max * shieldPercent);
        }

        // Flat-amount counterpart to ApplyShield above - takes an already-resolved Shield* since
        // every caller with one (ApplyShield's own lookup, ShieldEffectData's freshly-added-or-got
        // component) already has it, same shape HealUtility.ApplyFlatHeal uses. Caller is
        // responsible for bumping Shield.Max first if the grant should also raise capacity (see
        // ShieldEffectData.IncreaseMax) - this only ever tops up Current, capped at whatever Max
        // already is by the time it runs.
        public static void ApplyFlatShield(Frame f, EntityRef target, EntityRef owner, Shield* shield, FP amount)
        {
            if (amount <= FP._0)
                return;

            FP applied = FPMath.Min(amount, shield->Max - shield->Current);

            if (applied <= FP._0)
                return; // already at full Shield

            shield->Current += applied;

            // Temporary shield (Brute's Juggernaut - see Shield.qtn/docs/brute-ascensions.md): every
            // successful grant refreshes the ONE expiration timer back to its full configured
            // duration rather than adding a second, independent countdown - reset, not extended, per
            // the design spec. TemporaryDuration is 0 for everything that never opted in, so this is
            // a no-op for enemies/bosses and every other hero. Deliberately keyed off "any grant
            // landed here," not "this specific grant came from Juggernaut Discharge" - the single
            // shared entry point is what a Bodyguard payout or a Store Shield purchase land on too,
            // and refreshing here instead of only from Juggernaut avoids a grant surviving zero
            // ticks because the shared timer had already run out. Damage taken, weapon damage dealt
            // and movement never call this method, so none of them can ever refresh it.
            if (shield->TemporaryDuration > FP._0)
                shield->ExpirationRemaining = shield->TemporaryDuration;

            f.Events.EntityShielded(target, owner, applied);
        }

        // There is deliberately no Overshield (above-Max) entry point. It existed back when player
        // Shield refilled itself for free, where "stack a bit above Max" was the only way a grant
        // could feel meaningful. Player Shield is charge-only now (see Shield.qtn) - it starts empty
        // and only gameplay ever fills it - so a plain Max-capped pool already carries all the
        // scarcity the old 1.5x cap was there to create, and every grant funnels through the two
        // methods above.
    }
}
