namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Remix, line 3/3 on Resonance) - Zara's battlefield-control path.
    // Every third Resonance Pulse applies 1 (rank 1-2, strengthened at rank 2) or 2 distinct (rank 3
    // "Full Remix") randomly-chosen effects from this authored pool to every enemy the pulse damages -
    // see ResonanceUtility.FirePulse/ZaraRemixUtility. Rank 2 additionally starts the next Resonance
    // cycle partly charged.
    //
    // The status pool is fully data-driven (Slow/Burn/Stun/Rift Mark to start, but nothing here knows
    // which), each entry carrying its own rank-2 duration/magnitude multipliers (see RemixPoolEntry/
    // HitEffectData's 4-arg Apply overload) rather than Zara-specific per-status logic. Selection uses
    // deterministic frame RNG in the simulation only - the View is told which effect(s) were chosen
    // via the RemixPulseTriggered event and never rolls anything itself.
    public unsafe partial class RemixPassiveUpgradeData : PassiveUpgradeData
    {
        public List<RemixPoolEntry> Effects = new List<RemixPoolEntry>();

        [Tooltip("Rank 2+ - fraction of the Resonance threshold retained right after a Remix pulse. If Faster Tempo rank 3's own retention is also active, the HIGHER of the two applies - never both (see Resonance.qtn).")]
        public FP[] RetainFractionAfterRemix = { FP._0, FP._0_20, FP._0_20 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            resonance->RemixRank = (byte)rank;
            resonance->RemixRetainFraction = RetainFractionAfterRemix[System.Math.Clamp(rank, 1, (int)MaxRank) - 1];

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
