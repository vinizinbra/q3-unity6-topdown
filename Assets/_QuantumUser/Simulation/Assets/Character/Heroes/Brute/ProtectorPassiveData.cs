namespace Quantum
{
    using Photon.Deterministic;

    // Brute's base Passive - Protector. Adds the persistent, opt-in ProtectorAura component directly
    // onto his own entity (same "spawn-time bake adds a component" shape SeedShield/SeedArmor
    // already use in CharacterSystem, and the same "field lives on the hero" shape Kai's
    // ProjectileSlowField already uses) - the aura follows him automatically via his own Transform3D
    // (see ProtectorAuraSystem). A Passive Ascension mutates that component directly (see
    // LevelUp/Heroes/Brute/PassiveSkillUpgrades).
    public unsafe partial class ProtectorPassiveData : PassiveData
    {
        public FP Radius = 6;
        public FP IntimidateDamageMultiplier = FP._0_75;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new ProtectorAura
            {
                BaseRadius = Radius,
                Radius = Radius,
                IntimidateDamageMultiplier = IntimidateDamageMultiplier,
                AllyDamageReductionAmount = FP._0,
                IntimidateSlowMultiplier = FP._0,
                IntimidateKnockbackTakenMultiplier = FP._1,
                FearlessBonusVsIntimidated = FP._0,

                // 1 = no effect; only Guardian rank 2+ lowers it (allies take less knockback).
                AllyKnockbackTakenMultiplier = FP._1,

                // 0 = Guardian rank 3's reactive proc is off, which is what gates
                // BruteProtectorReactionSystem out entirely for a Brute who hasn't taken it.
                ReactiveDamageReductionAmount = FP._0,
                ReactiveDamageReductionDuration = FP._0,
                ReactiveCooldownPerAlly = FP._0,
            });
        }
    }
}
