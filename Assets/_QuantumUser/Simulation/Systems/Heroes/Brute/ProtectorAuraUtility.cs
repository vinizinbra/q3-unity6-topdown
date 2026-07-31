namespace Quantum
{
    using Photon.Deterministic;

    // Small static helpers for Brute's Protector Aura that don't belong on the per-tick
    // ProtectorAuraSystem itself - currently just Fearless, read from
    // DamageUtility.ResolveOutgoingDamage rather than applied by the aura system, since it needs
    // both the attacker (owner) and the target together at the moment damage resolves.
    public static unsafe class ProtectorAuraUtility
    {
        // Fearless - bonus damage Brute deals against an Intimidated target. 0
        // FearlessBonusVsIntimidated (the base passive's default) means the ascension hasn't been
        // taken.
        public static FP GetFearlessBonusMultiplier(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(owner, out var aura) == false)
                return FP._1;

            if (aura->FearlessBonusVsIntimidated <= FP._0 || StatusEffectUtility.IsIntimidated(f, target) == false)
                return FP._1;

            return FP._1 + aura->FearlessBonusVsIntimidated;
        }
    }
}
