namespace Quantum
{
    using Photon.Deterministic;

    // Haste-side counterpart to HasteEffectData, with its own Duration/AttackSpeedMultiplier rather
    // than reading the shared RuntimeConfig.EffectConfig defaults - lets Healing Chorus rank 2+/
    // Restorative Beat rank 2+ grant a short (~2s) Haste as specified, independent of whatever
    // duration every other Haste source in the game shares. Same StatusEffectUtility.ApplyHaste
    // entry point either way - only the source of the numbers differs.
    public unsafe class TimedHasteEffectData : HitEffectData
    {
        public FP Duration = 2;
        public FP AttackSpeedMultiplier = FP._1_50;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            StatusEffectUtility.ApplyHaste(f, context.Target, context.Owner, Duration, AttackSpeedMultiplier);
        }
    }
}
