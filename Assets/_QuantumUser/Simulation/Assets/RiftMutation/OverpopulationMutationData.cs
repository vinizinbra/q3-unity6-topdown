namespace Quantum
{
    using Photon.Deterministic;

    // Hordes of weaker enemies. RUN-SCOPE - it retunes the encounter itself, not the picker.
    //
    // Both halves go through the generic encounter modifiers rather than touching any individual
    // enemy definition: the density bonus scales the Director's budget, alive cap and target
    // pressure together (EncounterModifierUtility.ResolveSpawnDensityMultiplier), and the health
    // penalty is applied per spawn by EnemySystem.SeedHealth.
    //
    // Bosses are exempt from the health penalty - not by a check here, but because
    // ResolveEnemyHealthMultiplier ignores a NEGATIVE run-wide bonus for EnemyTier.Boss as a general
    // rule. A horde mutation shouldn't quietly halve a boss's health bar.
    public unsafe class OverpopulationMutationData : RiftMutationData
    {
        public FP SpawnDensityBonus = FP._0;
        public FP EnemyHealthBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.Global->EnemySpawnDensityBonus += SpawnDensityBonus;
            f.Global->EnemyMaxHealthBonus += EnemyHealthBonus;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            SpawnDensityBonus.AsFloat * 100f,
            EnemyHealthBonus.AsFloat * 100f
        };
    }
}
