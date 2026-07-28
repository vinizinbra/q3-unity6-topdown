namespace Quantum
{
    using Photon.Deterministic;

    // Grants a temporary attack-speed buff to the target - see StatusEffectUtility.ApplyHaste.
    // Duration/AttackSpeedMultiplier are read from the shared RuntimeConfig.EffectConfig rather than
    // authored here, so every source of Haste hits identically (same reasoning as
    // ExplodeOnDeathConfig). Doesn't check context.Target == context.Owner the way
    // Damage/Burn/Poison/Knockback do - a heal-triggered buff should be able to reach the owner same
    // as the heal itself.
    public unsafe class HasteEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
            {
                Log.Debug("[Effect] HasteEffectData skipped - no Target");
                return;
            }

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            StatusEffectUtility.ApplyHaste(f, context.Target, context.Owner, config.HasteDuration, config.HasteAttackSpeedMultiplier);

            Log.Debug($"[Effect] HasteEffectData applied to {context.Target}: {config.HasteDuration}s at x{config.HasteAttackSpeedMultiplier} attack speed");
        }
    }
}
