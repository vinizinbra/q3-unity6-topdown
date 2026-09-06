namespace Quantum
{
    using Photon.Deterministic;

    // Ice - multiplies movement speed for Duration. Plain overwrite-on-reapply, see
    // StatusEffectUtility.ApplyIce. Read by PlayerMovementProcessor and EnemySystem's chase movement.
    // Duration/SpeedMultiplier are read from the shared RuntimeConfig.EffectConfig rather than
    // authored here, so every source of Slow hits identically (same reasoning as HasteEffectData).
    public unsafe class SlowEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        // Zara's Remix ascension rank 2+ scales duration/magnitude generically through this overload
        // (see HitEffectData.Apply's own comment) - both default to FP._1 from the plain 2-arg Apply
        // above, reproducing the exact pre-Remix behavior for every other caller. magnitudeMultiplier
        // strengthens the slow itself (moves SpeedMultiplier further below 1, not above it) - a
        // multiplier on the REDUCTION (1 - SpeedMultiplier), not on SpeedMultiplier directly, so a
        // magnitudeMultiplier of 1 always reproduces the base config value exactly regardless of what
        // that value is.
        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            if (context.Target == EntityRef.None)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.SlowDuration) * durationMultiplier;
            FP speedMultiplier = FP._1 - (FP._1 - config.SlowSpeedMultiplier) * magnitudeMultiplier;

            StatusEffectUtility.ApplyIce(f, context.Target, duration, speedMultiplier);

            // Directly-authored Slow (not the weapon-elemental-proc path) still needs to participate
            // in the elemental reaction check for its own element.
            StatusEffectUtility.TryTriggerElementalReaction(f, context.Target, context.Owner, context.Source, ElementType.Ice, context.Damage);
        }
    }
}
