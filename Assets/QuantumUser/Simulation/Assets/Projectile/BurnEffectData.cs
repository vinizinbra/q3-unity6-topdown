namespace Quantum
{
    using Photon.Deterministic;

    // Refresh-only DoT - see StatusEffectUtility.ApplyBurn for what happens when this lands on an
    // already-burning target.
    public unsafe class BurnEffectData : HitEffectData
    {
        public FP Duration = 3;
        public FP DamagePercent = FP._0_10;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a burn
            // shouldn't hurt whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, Duration);
            FP damagePerTick = StatusEffectUtility.ComputeDotDamagePerTick(context.Damage, DamagePercent, Duration);

            StatusEffectUtility.ApplyBurn(f, context.Target, duration, damagePerTick, context.Owner, context.Source);
        }
    }
}
