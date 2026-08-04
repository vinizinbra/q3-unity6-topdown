namespace Quantum
{
    using Photon.Deterministic;

    // Fire Mastery trait - a Burning enemy already below its own tier's Health threshold is
    // executed outright the next time it takes Health damage (see
    // MaxFireMasteryReactionSystem.OnHealthDamageApplied). BossExecutionEnabled defaults false so
    // authoring a nonzero BossHealthThreshold alone can never accidentally execute a Boss.
    public unsafe partial class CremationPassiveUpgradeData : PassiveUpgradeData
    {
        public FP NormalHealthThreshold = FP._0_10;
        public FP EliteHealthThreshold = FP._0_10;
        public FP BossHealthThreshold = FP._0_05;
        public bool BossExecutionEnabled;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<ExecuteAgainstStatus>(entity, out var execute);
            execute->NormalHealthThreshold = FPMath.Max(execute->NormalHealthThreshold, NormalHealthThreshold);
            execute->EliteHealthThreshold = FPMath.Max(execute->EliteHealthThreshold, EliteHealthThreshold);
            execute->BossHealthThreshold = FPMath.Max(execute->BossHealthThreshold, BossHealthThreshold);
            execute->BossExecutionEnabled |= BossExecutionEnabled;
        }
    }
}
