namespace Quantum
{
    using Photon.Deterministic;

    // Ground-hazard slow - authored onto a lingering AreaDamage (World 2 Tar Launcher's tar puddle),
    // which re-applies it every TickInterval to whoever stands inside. Keep Duration a little longer
    // than the area's TickInterval so the slow never flickers off between ticks, and short enough
    // that stepping out restores speed almost at once. Not Ice: never builds toward Freeze or feeds
    // elemental reactions - see StatusEffectUtility.ApplyStickySlow.
    public unsafe class StickySlowEffectData : HitEffectData
    {
        public FP Duration = FP._0_25;

        // Fraction of normal move speed kept while slowed (0.65 = 35% slower).
        public FP SpeedMultiplier = FP.FromString("0.65");

        public bool OnlyWhenGrounded = true;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            // A ground puddle only grips feet - jumping/hopping over it while airborne is free.
            if (OnlyWhenGrounded == true &&
                f.Unsafe.TryGetPointer<KCC>(context.Target, out var kcc) == true &&
                kcc->Data.IsGrounded == false)
                return;

            StatusEffectUtility.ApplyStickySlow(f, context.Target, Duration, SpeedMultiplier);
        }
    }
}
