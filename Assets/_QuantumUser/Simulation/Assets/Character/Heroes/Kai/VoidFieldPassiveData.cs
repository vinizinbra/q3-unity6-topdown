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
        public FP Radius = 4;
        public FP SpeedMultiplier = FP._0_50;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new ProjectileSlowField
            {
                Radius = Radius,
                SpeedMultiplier = SpeedMultiplier,
                EnemyTimeDilationMultiplier = FP._0,
            });
        }
    }
}
