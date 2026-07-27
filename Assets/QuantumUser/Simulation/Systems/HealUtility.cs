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

            if (health->CurrentHealth <= FP._0)
                return; // dead or never seeded - nothing to heal

            FP requested = health->MaxHealth * healPercent * ResolveHealMultiplier(f, owner);

            if (requested <= FP._0)
                return;

            FP applied = FPMath.Min(requested, health->MaxHealth - health->CurrentHealth);

            if (applied <= FP._0)
                return; // already at full health

            health->CurrentHealth += applied;
            f.Events.EntityHealed(target, owner, applied);

            Log.Debug($"[Heal] {target} healed for {applied} -> {health->CurrentHealth}/{health->MaxHealth}");
        }

        // IncreaseHealUpgrade (see Heroes/Zara/IncreaseHealSkillAction) - read live on every heal
        // application rather than baked in anywhere. Unlike PoisonDamageWavesUpgrade/
        // HasteOnHealUpgrade, ApplyHeal already receives owner on every single call, so there's no
        // spawn-time race to work around - this is simply re-checked fresh each heal. FP._1 for
        // anyone who doesn't hold it, so an unmodified heal applies at exactly its authored
        // HealPercent.
        private static FP ResolveHealMultiplier(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<IncreaseHealUpgrade>(owner, out var upgrade) == true ? FP._1 + upgrade->HealBonus : FP._1;
        }
    }
}
