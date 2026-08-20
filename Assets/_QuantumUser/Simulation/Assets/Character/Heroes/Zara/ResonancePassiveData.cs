namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Zara's base Passive - Resonance. Adds the persistent, opt-in Resonance component directly
    // onto her own entity (same "spawn-time bake adds a component" shape SeedShield/SeedArmor
    // already use in CharacterSystem, and the same "field lives on the hero" shape Kai's
    // ProjectileSlowField/Brute's ProtectorAura already use). A Passive Ascension mutates that
    // component directly (see LevelUp/Heroes/Zara/PassiveSkillUpgrades) rather than CharacterStats,
    // since none of these tunables are generic hero stats.
    public unsafe partial class ResonancePassiveData : PassiveData
    {
        [Tooltip("Resonance needed for one Pulse. Tune this together with GenerationPerDamage against a target cadence rather than in isolation - the design target is roughly one Pulse every 10-12s for baseline Zara in active combat, which is a function of both plus her actual DPS.")]
        public FP Max = 500;

        [Tooltip("Resonance gained per point of eligible damage dealt. Pulse damage itself never generates Resonance (DamageUtility.ApplyDamage's generatesResonance: false) - see ResonanceUtility.")]
        public FP GenerationPerDamage = FP._1;

        public FP Radius = 5;

        [Tooltip("Small emergency heal per Pulse, as a fraction of the ally's own MaxHealth. Deliberately NOT scaled by any Ascension - healing is secondary for Zara, and Protective Rhythm buys Shield/mitigation instead.")]
        public FP HealPercent = FP.FromString("0.02");

        public FP DamageAmount = 15;

        // Every pulse is a genuine shockwave from the start. Small/Medium/Strong is the same shared
        // KnockbackTier ladder every other push in the game already uses (see
        // EffectConfig.GetKnockback) - Zara doesn't get her own bespoke magnitude. No Ascension raises
        // this any more (Heavy Bass, which used to, was removed - Amplifier is her offensive path).
        public KnockbackTier KnockbackTier = KnockbackTier.Small;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new Resonance
            {
                Current = FP._0,
                Max = Max,
                GenerationPerDamage = GenerationPerDamage,
                Radius = Radius,
                HealPercent = HealPercent,
                DamageAmount = DamageAmount,
                KnockbackTier = (byte)KnockbackTier,
                PulseCount = 0,

                // Every Ascension-owned field starts off - Protective Rhythm/Faster Tempo/Remix each
                // turn on their own half. Written explicitly rather than left to the component's
                // zero-init so the full contract is visible in one place.
                OvershieldPercentOfMaxShield = FP._0,
                OvershieldCapMultiplier = FP._1_50,
                DamageReductionAmount = FP._0,
                DamageReductionDuration = FP._0,
                RetainFraction = FP._0,
                RemixRetainFraction = FP._0,
                RemixRank = 0,
            });
        }
    }
}
