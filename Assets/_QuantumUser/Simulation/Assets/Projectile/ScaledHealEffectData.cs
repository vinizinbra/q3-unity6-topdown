namespace Quantum
{
    using Photon.Deterministic;

    // Heal-side mirror of DamageEffectData - scales whatever percent-of-MaxHealth AlternatingArea
    // already resolved into HitEffectContext.Damage (see AlternatingAreaSystem's heal-phase branch/
    // SpawnAlternatingAreaEffectData.ResolveHealAmount), rather than carrying its own fixed percent
    // the way the older HealEffectData does. Lets Zara's Totem/Portable Speaker Healing Beats share
    // one baked-per-cast percent (ranked by Healing Chorus) across a whole HealEffects list, the same
    // "one FP on the component, effects just scale it" shape the Damage side already had.
    public unsafe class ScaledHealEffectData : HitEffectData
    {
        public FP HealMultiplier = FP._1;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<Health>(context.Target, out var health) == false)
                return;

            FP requested = health->MaxHealth * context.Damage * HealMultiplier;

            // Per-deployable-instance healing cap (Zara's "20% Max HP per Totem per ally") - a no-op
            // for any area that doesn't carry an AreaAllyBudget, which is every one that didn't opt
            // in. Once the allowance is spent this returns 0 and the Support Beat still delivers
            // everything else in its Effects list (Move Speed, Fire Rate, cooldown reduction) with
            // only the HP half switched off, exactly as specified.
            FP allowed = AreaAllyBudgetUtility.ConsumeHeal(f, context.SourceEntity, context.Target, requested, health->MaxHealth);

            if (allowed <= FP._0)
                return;

            HealUtility.ApplyFlatHeal(f, context.Target, context.Owner, health, allowed);
        }
    }
}
