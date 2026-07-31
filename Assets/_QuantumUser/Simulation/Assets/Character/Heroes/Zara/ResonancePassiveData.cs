namespace Quantum
{
    using Photon.Deterministic;

    // Zara's base Passive - Resonance. Adds the persistent, opt-in Resonance component directly
    // onto her own entity (same "spawn-time bake adds a component" shape SeedShield/SeedArmor
    // already use in CharacterSystem, and the same "field lives on the hero" shape Kai's
    // ProjectileSlowField/Brute's ProtectorAura already use). A Passive Ascension mutates that
    // component directly (see LevelUp/Heroes/Zara/PassiveSkillUpgrades) rather than CharacterStats,
    // since none of these tunables are generic hero stats.
    public unsafe partial class ResonancePassiveData : PassiveData
    {
        public FP Max = 100;
        public FP GenerationPerDamage = FP._1;
        public FP Radius = 5;
        public FP HealPercent = FP._0_10;
        public FP DamageAmount = 15;

        // Every pulse is a genuine shockwave from the start, not just a heal/damage burst - Heavy
        // Bass raises this by one tier (see HeavyBassPassiveUpgradeData) rather than being the only
        // source of knockback. Small/Medium/Strong is the same shared KnockbackTier ladder every
        // other push in the game already uses (see EffectConfig.GetKnockback) - Zara doesn't get her
        // own bespoke magnitude.
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
            });
        }
    }
}
