namespace Quantum
{
    using Photon.Deterministic;

    // Freezes the target's state machine/movement/firing for Duration - see
    // StatusEffectUtility.IsStunned, read by EnemySystem, PlayerMovementProcessor and WeaponSystem.
    // Plain overwrite-on-reapply.
    public unsafe class StunEffectData : HitEffectData
    {
        public FP Duration = 1;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a stun
            // shouldn't stun whoever set it off, same as Damage/Burn/Poison/Knockback.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, Duration);

            StatusEffectUtility.ApplyStun(f, context.Target, duration);

            Log.Debug($"[Effect] StunEffectData applied to {context.Target} for {duration}s");
        }
    }
}
