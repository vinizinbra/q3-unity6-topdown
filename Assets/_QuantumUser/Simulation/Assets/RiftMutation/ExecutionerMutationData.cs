namespace Quantum
{
    using Photon.Deterministic;

    // A threshold-triggered KILL, not bonus damage: after this player's own damage lands on an enemy,
    // if what it has left falls below its tier's own HP% threshold, it is immediately finished off -
    // through the exact same kill pipeline a lethal hit resolves through
    // (DamageUtility.TryExecute/ResolveDeath - normal kill attribution, drops, on-kill signals, weapon
    // perks, everything).
    //
    // One threshold per EnemyTier (CharacterStats.ExecutionThresholds, indexed by (int)EnemyTier) -
    // tougher tiers get a much lower bar, since a flat percentage of a Boss's health is a far bigger
    // effective HP swing than the same percentage of a Filler's. Checked only against damage whose
    // OWNER holds this mutation (RiftMutationReactionSystem.OnHealthDamageApplied), so a teammate's
    // hit alone can never trigger someone else's execution.
    public unsafe class ExecutionerMutationData : RiftMutationData
    {
        public FP FillerThreshold = FP._0;
        public FP NormalThreshold = FP._0;
        public FP SpecialistThreshold = FP._0;
        public FP HeavyThreshold = FP._0;
        public FP EliteThreshold = FP._0;
        public FP BossThreshold = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            var thresholds = stats->ExecutionThresholds;

            thresholds[(int)EnemyTier.Filler] = FPMath.Max(thresholds[(int)EnemyTier.Filler], FillerThreshold);
            thresholds[(int)EnemyTier.Normal] = FPMath.Max(thresholds[(int)EnemyTier.Normal], NormalThreshold);
            thresholds[(int)EnemyTier.Specialist] = FPMath.Max(thresholds[(int)EnemyTier.Specialist], SpecialistThreshold);
            thresholds[(int)EnemyTier.Heavy] = FPMath.Max(thresholds[(int)EnemyTier.Heavy], HeavyThreshold);
            thresholds[(int)EnemyTier.Elite] = FPMath.Max(thresholds[(int)EnemyTier.Elite], EliteThreshold);
            thresholds[(int)EnemyTier.Boss] = FPMath.Max(thresholds[(int)EnemyTier.Boss], BossThreshold);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            FillerThreshold.AsFloat * 100f,
            HeavyThreshold.AsFloat * 100f,
            BossThreshold.AsFloat * 100f
        };
    }
}
