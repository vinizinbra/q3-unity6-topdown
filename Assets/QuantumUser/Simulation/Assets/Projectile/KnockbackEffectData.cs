namespace Quantum
{
    using Photon.Deterministic;

    // Owns the push it deals - unlike DamageEffectData, which scales a number the hit already
    // decided on. Knockback has no per-attack "how hard" to inherit: authoring it here means an
    // attack's Effects list is the one place that decides whether and how hard it pushes.
    //
    // Fires every time the containing source resolves a hit - once for a single-shot attack, but
    // every TickInterval for an AreaDamage. Drop this into a lingering area's Effects only if you
    // want the target juggled for the area's whole lifetime; a one-tick blast is the safe case.
    public unsafe class KnockbackEffectData : HitEffectData
    {
        // X: horizontal push. Y: vertical pop - ground friction is ~20x air friction, so X alone
        // gets eaten while grounded unless this briefly launches the target airborne - keep Y
        // nonzero even when only tuning X. See DamageUtility.ApplyKnockback.
        public FP Force = 4;
        public FP UpwardForce = 4;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a blast
            // shouldn't push whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            DamageUtility.ApplyKnockback(f, context.Target, context.PushDirection, Force, UpwardForce, context.Owner);
        }
    }
}
