namespace Quantum
{
    using Photon.Deterministic;

    // Ice - multiplies movement speed for Duration. Plain overwrite-on-reapply, see
    // StatusEffectUtility.ApplyIce. Read by PlayerMovementProcessor and EnemySystem's chase movement.
    // Duration/SpeedMultiplier are read from the shared RuntimeConfig.EffectConfig rather than
    // authored here, so every source of Slow hits identically (same reasoning as HasteEffectData).
    public unsafe class SlowEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.SlowDuration);

            StatusEffectUtility.ApplyIce(f, context.Target, duration, config.SlowSpeedMultiplier);

            // Directly-authored Slow (not the weapon-elemental-proc path) still needs to participate
            // in the Rift Mark reaction check, using the same pre-hit snapshot the weapon-proc path
            // uses - see HitEffectContext.PreHitRiftMarkStacks' own comment.
            StatusEffectUtility.TryConsumeRiftMarkReaction(f, context.Target, context.Owner, context.Source, ElementType.Ice, context.Damage, context.PreHitRiftMarkStacks);
        }
    }
}
