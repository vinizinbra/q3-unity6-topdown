namespace Quantum
{
    using Photon.Deterministic;

    // Healing Chorus rank 3 "Encore" / Restorative Beat's own overheal-to-Shield mechanic - heals
    // AND converts the heal's own excess (whatever didn't fit under the target's MaxHealth) into
    // Overshield in one call, replacing ScaledHealEffectData in a Totem/Portable Speaker's
    // HealEffects slot 0 at rank 3 rather than needing a second list entry to coordinate with.
    // context.Damage carries the heal PERCENT (same convention ScaledHealEffectData uses, seeded
    // from AlternatingArea.HealAmount) - requested is computed pre-owner-heal-multiplier (the
    // nominal ask, not what ResolveHealMultiplier inside ApplyFlatHeal ultimately lets through), a
    // decisive simplification rather than threading that multiplier through the excess calc too.
    public unsafe class OverhealToShieldEffectData : HitEffectData
    {
        public FP ShieldConversionPercent = FP._0_50;
        public FP OvershieldCapMultiplier = FP._1_50;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<Health>(context.Target, out var health) == false)
                return;

            FP requested = health->MaxHealth * context.Damage;

            // Same per-deployable-instance healing cap ScaledHealEffectData respects (see
            // AreaAllyBudgetUtility) - the cap governs HP restored, so the excess that converts to
            // Shield is computed against the ALLOWED ask, not the nominal one. Once the allowance is
            // spent this contributes nothing, HP or Shield.
            requested = AreaAllyBudgetUtility.ConsumeHeal(f, context.SourceEntity, context.Target, requested, health->MaxHealth);

            if (requested <= FP._0)
                return;

            FP applied = HealUtility.ApplyFlatHeal(f, context.Target, context.Owner, health, requested);
            FP excess = requested - applied;

            if (excess <= FP._0)
                return;

            ShieldUtility.ApplyOvershield(f, context.Target, context.Owner, excess * ShieldConversionPercent, OvershieldCapMultiplier);
        }
    }
}
