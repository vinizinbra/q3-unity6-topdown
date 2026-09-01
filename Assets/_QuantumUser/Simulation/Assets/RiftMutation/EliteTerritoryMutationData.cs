namespace Quantum
{
    using Photon.Deterministic;

    // Fewer enemies, but considerably more dangerous ones. RUN-SCOPE, the mirror of Overpopulation.
    //
    // Implemented purely as spawn SELECTION weighting - the Director's existing weighted group roll
    // simply favours groups containing an Elite-or-higher member
    // (EncounterModifierUtility.ResolveGroupWeightMultiplier, reusing CombatDirectorUtility's own
    // GroupContainsMajor test). Nothing substitutes or upgrades enemy types after the fact, so
    // encounter design stays entirely in the EnemyGroupConfig assets.
    //
    // Boss spawning is unaffected: a Boss phase stops Director pulses entirely, so the only rolls
    // this can ever reach are normal combat ones.
    public unsafe class EliteTerritoryMutationData : RiftMutationData
    {
        public FP SpawnDensityBonus = FP._0;
        public FP EliteWeightMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.Global->EnemySpawnDensityBonus += SpawnDensityBonus;

            // Take-the-stronger rather than compounding - the field is a raw multiplier, and two
            // sources multiplying into a 6x elite bias would be far outside the intended range.
            f.Global->EliteGroupWeightMultiplier = FPMath.Max(f.Global->EliteGroupWeightMultiplier, EliteWeightMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            SpawnDensityBonus.AsFloat * 100f,
            EliteWeightMultiplier.AsFloat
        };
    }
}
