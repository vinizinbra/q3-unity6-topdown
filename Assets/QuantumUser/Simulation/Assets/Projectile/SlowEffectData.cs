namespace Quantum
{
    using Photon.Deterministic;

    // Ice - multiplies movement speed for Duration. Plain overwrite-on-reapply, see
    // StatusEffectUtility.ApplyIce. Read by PlayerMovementProcessor and EnemySystem's chase movement.
    public unsafe class SlowEffectData : HitEffectData
    {
        public FP Duration = 3;
        public FP SpeedMultiplier = FP._0_50;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, Duration);

            StatusEffectUtility.ApplyIce(f, context.Target, duration, SpeedMultiplier);
        }
    }
}
