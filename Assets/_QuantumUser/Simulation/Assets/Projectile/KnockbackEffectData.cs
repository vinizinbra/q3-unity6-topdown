namespace Quantum
{
    using Photon.Deterministic;

    // Owns the push it deals - unlike DamageEffectData, which scales a number the hit already
    // decided on. Knockback has no per-attack "how hard" to inherit: authoring Tier here means an
    // attack's Effects list is the one place that decides whether and how hard it pushes. Force/
    // UpwardForce themselves are read from the shared RuntimeConfig.EffectConfig (see
    // EffectConfig.GetKnockback) rather than authored here, so every source of a given Tier pushes
    // identically (same reasoning as HasteEffectData) - only Direction/Tier are ever authored per use.
    //
    // Fires every time the containing source resolves a hit - once for a single-shot attack, but
    // every TickInterval for an AreaDamage. Drop this into a lingering area's Effects only if you
    // want the target juggled for the area's whole lifetime; a one-tick blast is the safe case.
    public unsafe class KnockbackEffectData : HitEffectData
    {
        public KnockbackTier Tier = KnockbackTier.Medium;

        // Additive (default): stacks with any other knockback landing the same tick - correct for a
        // single hard-hitting hit. Override: replaces the target's existing push instead - set this
        // on a source that can land several hits itself in one tick/quick succession (e.g. a
        // multi-pellet shotgun's per-pellet KnockbackEffectData), so pellet count doesn't multiply
        // how far the target flies.
        public KnockbackApplyMode ApplyMode = KnockbackApplyMode.Additive;

        // Orthogonal to Tier (which only sets magnitude): whether landing this push is also allowed
        // to cancel whatever the target enemy is currently doing (see Enemy.qtn's
        // OnEnemyKnockedBack/EnemySystem.OnEnemyKnockedBack). Default true reproduces every existing
        // asset's current behavior unchanged. False on the shared KnockbackSmallEffectData used by
        // basic weapon fire - that push should read as a juicy impact without ever throwing away an
        // enemy's windup/active attack.
        public bool CanInterrupt = true;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a blast
            // shouldn't push whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            config.GetKnockback(Tier, out FP force, out FP upwardForce);

            DamageUtility.ApplyKnockback(f, context.Target, context.PushDirection, force, upwardForce, context.Owner,
                ApplyMode, CanInterrupt);
        }
    }
}
