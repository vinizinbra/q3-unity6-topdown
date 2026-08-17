namespace Quantum
{
    using Photon.Deterministic;

    // Freezes the target's state machine/movement/firing for Duration - see
    // StatusEffectUtility.IsStunned, read by EnemySystem, PlayerMovementProcessor and WeaponSystem.
    // Plain overwrite-on-reapply. Duration is read from the shared RuntimeConfig.EffectConfig rather
    // than authored here, so every source of Stun hits identically (same reasoning as HasteEffectData).
    public unsafe class StunEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        // Zara's Remix ascension rank 2+ scales duration generically through this overload (see
        // HitEffectData.Apply's own comment) - defaults to FP._1 from the plain 2-arg Apply above,
        // reproducing the exact pre-Remix behavior for every other caller. magnitudeMultiplier is
        // unused/ignored - Stun has no separate magnitude axis to strengthen.
        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a stun
            // shouldn't stun whoever set it off, same as Damage/Burn/Poison/Knockback.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.StunDuration) * durationMultiplier;

            StatusEffectUtility.ApplyStun(f, context.Target, duration, context.Owner);

            Log.Debug($"[Effect] StunEffectData applied to {context.Target} for {duration}s");
        }
    }
}
