namespace Quantum
{
    using Photon.Deterministic;

    // Remix ascension's own apply dispatcher - see ResonanceUtility.FirePulse. Stays a thin, generic
    // wrapper around HitEffectData's own 4-arg Apply overload (see that method's own comment) rather
    // than a switch on concrete effect type, so "each status definition provides its own Rank 2
    // multiplier" holds - Remix never needs to know which of Burn/Slow/Stun/Rift Mark it drew.
    public static unsafe class ZaraRemixUtility
    {
        public static void ApplyRemixEffect(Frame f, ref HitEffectContext context, RemixPoolEntry entry, int remixRank)
        {
            if (entry.Effect.IsValid == false)
                return;

            FP durationMultiplier = remixRank >= 2 ? entry.Rank2DurationMultiplier : FP._1;
            FP magnitudeMultiplier = remixRank >= 2 ? entry.Rank2MagnitudeMultiplier : FP._1;

            f.FindAsset(entry.Effect).Apply(f, ref context, durationMultiplier, magnitudeMultiplier);
        }
    }
}
