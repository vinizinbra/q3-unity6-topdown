namespace Quantum
{
    using Photon.Deterministic;

    // Heal counterpart to DamageUtility.ApplyDamage - much smaller since there's no armor/shield/
    // crit/status layer to mirror, just a capped addition to CurrentHealth.
    public static unsafe class HealUtility
    {
        // healPercent is a fraction of the TARGET's own MaxHealth (same convention as
        // BurnEffectData.DamagePercent/ExplodeOnDeathConfig.DamagePercent), not the healer's own
        // stats - a squishy ally and a tank both get healed proportionally to their own pool with no
        // separate tuning.
        public static void ApplyHeal(Frame f, EntityRef target, EntityRef owner, FP healPercent)
        {
            if (f.Unsafe.TryGetPointer<Health>(target, out var health) == false)
                return;

            ApplyFlatHeal(f, target, owner, health, health->MaxHealth * healPercent);
        }

        // Flat-amount counterpart to ApplyHeal above - HealthRegenSystem heals by an authored FP/sec
        // rate rather than a percent of MaxHealth, so it shares this core instead of going through
        // ApplyHeal's percent-of-max conversion. Takes an already-resolved Health* since every
        // caller (ApplyHeal's own lookup, HealthRegenSystem's filter) already has one.
        // Returns the amount actually applied (post-multiplier, capped at however much headroom the
        // target had left) - Zara's Healing Chorus/Restorative Beat rank 3 (Encore/"Restorative
        // Beat") both need this to compute their own overheal-to-Shield conversion; every
        // pre-existing caller simply ignores the return value, zero behavior change for them.
        public static FP ApplyFlatHeal(Frame f, EntityRef target, EntityRef owner, Health* health, FP amount)
        {
            if (health->CurrentHealth <= FP._0)
                return FP._0; // dead or never seeded - nothing to heal

            FP requested = amount * ResolveHealMultiplier(f, owner);

            if (requested <= FP._0)
                return FP._0;

            FP applied = FPMath.Min(requested, health->MaxHealth - health->CurrentHealth);

            if (applied <= FP._0)
                return FP._0; // already at full health

            health->CurrentHealth += applied;
            f.Events.EntityHealed(target, owner, applied);

            Log.Debug($"[Heal] {target} healed for {applied} -> {health->CurrentHealth}/{health->MaxHealth}");

            return applied;
        }

        // Read live on every heal application - ApplyHeal already receives owner on every single
        // call, so there's no spawn-time race to work around, this is simply re-checked fresh each
        // heal. Stacks with CharacterStats.HealingReceivedMultiplier (1 for anything without
        // CharacterStats, e.g. an enemy heal effect) rather than replacing it - the two are
        // independent sources, same "stack, don't replace" convention as GetSourceMultiplier in
        // DamageUtility.
        private static FP ResolveHealMultiplier(Frame f, EntityRef owner)
        {
            FP multiplier = FP._1;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true)
            {
                multiplier *= stats->HealingReceivedMultiplier;
            }

            return multiplier;
        }
    }
}
