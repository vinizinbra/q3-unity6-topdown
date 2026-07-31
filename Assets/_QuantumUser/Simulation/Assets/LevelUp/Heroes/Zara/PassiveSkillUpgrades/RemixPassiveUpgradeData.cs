namespace Quantum
{
    using System.Collections.Generic;

    // Passive Ascension - every third Resonance Pulse also applies one randomly-chosen HitEffectData
    // from this authored pool to every enemy the pulse damages - see ResonanceUtility.
    // ResolveRemixEffect. Reuses whatever generic HitEffectData assets already exist (Burn/Void/
    // Slow/Stun, all already zero-config since they read their own magnitudes from the shared
    // RuntimeConfig.EffectConfig) instead of inventing Remix-specific behavior.
    public unsafe partial class RemixPassiveUpgradeData : PassiveUpgradeData
    {
        public List<AssetRef<HitEffectData>> Effects = new List<AssetRef<HitEffectData>>();

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            var slots = resonance->RemixEffects;
            int count = Effects.Count < slots.Length ? Effects.Count : slots.Length;

            if (Effects.Count > slots.Length)
            {
                Log.Error($"[Resonance] Remix authored {Effects.Count} effects but Resonance.RemixEffects only holds {slots.Length} - the rest are dropped");
            }

            for (int i = 0; i < count; i++)
            {
                slots[i] = Effects[i];
            }
        }
    }
}
