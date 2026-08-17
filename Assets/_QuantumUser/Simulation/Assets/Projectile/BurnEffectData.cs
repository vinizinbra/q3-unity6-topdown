namespace Quantum
{
    using Photon.Deterministic;

    // Refresh-only DoT - see StatusEffectUtility.ApplyBurn for what happens when this lands on an
    // already-burning target. Duration/DamagePercent are read from the shared RuntimeConfig.EffectConfig
    // rather than authored here, so every source of Burn hits identically (same reasoning as HasteEffectData).
    public unsafe class BurnEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        // Zara's Remix ascension rank 2+ scales duration/magnitude generically through this overload
        // (see HitEffectData.Apply's own comment) - both default to FP._1 from the plain 2-arg Apply
        // above, reproducing the exact pre-Remix behavior for every other caller.
        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a burn
            // shouldn't hurt whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.BurnDuration) * durationMultiplier;
            FP damagePerTick = StatusEffectUtility.ComputeDotDamagePerTickWithFloor(f, context.Owner, context.Damage,
                config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval) * magnitudeMultiplier;

            StatusEffectUtility.ApplyBurn(f, context.Target, duration, damagePerTick, context.Owner, context.Source, config.TickInterval);

            // Directly-authored Burn (not the weapon-elemental-proc path) still needs to participate
            // in the Rift Mark reaction check, using the same pre-hit snapshot the weapon-proc path
            // uses - see HitEffectContext.PreHitRiftMarkStacks' own comment.
            StatusEffectUtility.TryConsumeRiftMarkReaction(f, context.Target, context.Owner, context.Source, ElementType.Fire, context.Damage, context.PreHitRiftMarkStacks);
        }
    }
}
