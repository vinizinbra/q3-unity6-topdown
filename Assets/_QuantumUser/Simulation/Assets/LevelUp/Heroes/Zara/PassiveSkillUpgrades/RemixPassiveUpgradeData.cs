namespace Quantum
{
    using System.Collections.Generic;

    // Passive Ascension (Remix, ranked, line 4/4 on Resonance) - see docs/zara-ascensions.md. Every
    // third Resonance Pulse applies 1 (rank 1-2, strengthened at rank 2) or 2 distinct (rank 3 "Full
    // Remix") randomly-chosen effects from this authored pool to every enemy the pulse damages - see
    // ResonanceUtility.FirePulse/ZaraRemixUtility. Each pool entry carries its own rank-2 duration/
    // magnitude multipliers (see RemixPoolEntry/HitEffectData's 4-arg Apply overload) rather than
    // Zara-specific per-status logic.
    public unsafe partial class RemixPassiveUpgradeData : PassiveUpgradeData
    {
        public List<RemixPoolEntry> Effects = new List<RemixPoolEntry>();

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            resonance->RemixRank = (byte)rank;

            var slots = resonance->RemixPool;
            int count = Effects.Count < slots.Length ? Effects.Count : slots.Length;

            if (Effects.Count > slots.Length)
            {
                Log.Error($"[Resonance] Remix authored {Effects.Count} effects but Resonance.RemixPool only holds {slots.Length} - the rest are dropped");
            }

            for (int i = 0; i < count; i++)
            {
                slots[i] = Effects[i];
            }
        }
    }
}
