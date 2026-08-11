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
                HasReactiveDamageReduction = false,
            });
        }
    }
}
