namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DamageEffectData : HitEffectData
    {
        // Scales the projectile's own damage, so a secondary/splash effect can hit for a fraction
        // of the shot without re-authoring the number.
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a blast
            // shouldn't hurt whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            DamageUtility.ApplyDamage(f, context.Target, context.Damage * DamageMultiplier,
                context.Owner, context.Source);
        }
    }
}
