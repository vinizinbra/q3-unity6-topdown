namespace Quantum
{
    using Photon.Deterministic;

    // Kai's base Passive - Void Field. Adds the persistent, opt-in ProjectileSlowField component
    // directly onto Kai's own entity (same "spawn-time bake adds a component" shape SeedShield/
    // SeedArmor already use) - the field follows him automatically via his own Transform3D (see
    // VoidFieldSystem), no separate tracking entity needed. Only the SlowArea dash ascension needs
    // a standalone dropped instance.
    public unsafe partial class VoidFieldPassiveData : PassiveData
    {
        public FP Radius = FP.FromString("2.5");

        // Baseline projectile speed while caught (60% - a 40% slow). Event Horizon's own ranked
        // SpeedMultiplierBonus subtracts from THIS, not from whatever a previous rank left behind -
        // see ProjectileSlowField.BaseSpeedMultiplier's own comment.
        public FP SpeedMultiplier = FP.FromString("0.60");

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new ProjectileSlowField
            {
                BaseRadius = Radius,
                Radius = Radius,
                BaseSpeedMultiplier = SpeedMultiplier,
                SpeedMultiplier = SpeedMultiplier,
                EnemyTimeDilationMultiplier = FP._0,

                // Filler/Normal/Specialist only, never Elite/Boss - preserves the pre-Event-Horizon-
                // refactor default exactly (see that ascension's own rank 3 "Void Pressure").
                MaxAffectedEnemyTierIndex = (byte)EnemyTier.Specialist,
            });
        }
    }
}
