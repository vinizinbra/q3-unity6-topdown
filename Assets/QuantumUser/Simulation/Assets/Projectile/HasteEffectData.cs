namespace Quantum
{
    using Photon.Deterministic;

    // Grants a temporary attack-speed buff to the target - see StatusEffectUtility.ApplyHaste.
    // Duration/AttackSpeedMultiplier are read from the shared RuntimeConfig.HasteConfig rather than
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

            HasteConfig config = f.FindAsset(f.RuntimeConfig.HasteConfig);

            if (config == null)
            {
                Log.Error("[Effect] HasteEffectData couldn't resolve RuntimeConfig.HasteConfig - is it assigned in the RuntimeConfig asset?");
                return;
            }

            StatusEffectUtility.ApplyHaste(f, context.Target, context.Owner, config.Duration, config.AttackSpeedMultiplier);

            Log.Debug($"[Effect] HasteEffectData applied to {context.Target}: {config.Duration}s at x{config.AttackSpeedMultiplier} attack speed");
        }
    }
}
