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
            f.Events.EntityShielded(target, owner, applied);
        }

        // Overshield counterpart to ApplyShield/ApplyFlatShield above - capped at shield->Max *
        // capMultiplier rather than shield->Max itself, so Current can still sit above Max (e.g.
        // Brute's Discharge Shield gain caps at 1.5x Max, not 1x) without growing unbounded. Nothing
        // else needed to support the "above Max" half: ShieldSystem's passive regen already no-ops
        // whenever Current >= Max, and DamageUtility.AbsorbWithShield already drains Current by a
        // plain Min(Current, damage) regardless of how far above Max it is - the overshield just
        // bleeds off as damage is taken, same as regular Shield.
        public static void ApplyOvershield(Frame f, EntityRef target, EntityRef owner, FP amount, FP capMultiplier)
        {
            if (amount <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Shield>(target, out var shield) == false)
                return;

            FP cap = shield->Max * capMultiplier;
            FP applied = FPMath.Min(amount, cap - shield->Current);

            if (applied <= FP._0)
                return; // already at or above the overshield cap

            shield->Current += applied;
            f.Events.EntityShielded(target, owner, applied);
        }
    }
}
