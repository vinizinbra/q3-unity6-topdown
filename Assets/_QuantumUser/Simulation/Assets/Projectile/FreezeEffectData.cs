namespace Quantum
{
    using Photon.Deterministic;

    // Stretches the target's own attack anticipation/windup (StatusEffectUtility.
    // ApplyAnticipationSlow/GetAnticipationMultiplier) for Duration - not a lockout, see
    // docs/elemental-reactions.md's "Freeze: stretching anticipation, not stopping the target".
    // Plain overwrite-on-reapply. Duration/Multiplier are read from
    // RuntimeConfig.EffectConfig.AnticipationSlowDuration/AnticipationSlowMultiplier - deliberately
    // NOT ElementalReactionConfig's FreezeDuration/FreezeAnticipationMultiplier, which are dedicated
    // to the Void+Ice reaction and would otherwise get silently retuned by any other source using
    // this class. Gives Freeze a freely-authorable HitEffectData the same way Stun/Root already
    // have one, independent of the elemental reaction that normally grants it.
    public unsafe class FreezeEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a freeze
            // shouldn't freeze whoever set it off, same as Damage/Burn/Stun/Root/Knockback.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.AnticipationSlowDuration);

            StatusEffectUtility.ApplyAnticipationSlow(f, context.Target, duration, config.AnticipationSlowMultiplier);

            Log.Debug($"[Effect] FreezeEffectData applied to {context.Target} for {duration}s");
        }
    }
}
