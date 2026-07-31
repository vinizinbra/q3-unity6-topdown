namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the Void Field's projectile slow (lowers SpeedMultiplier
    // further, floored at 0 so it can never reverse into a speed boost).
    public unsafe partial class TimeDilationPassiveUpgradeData : PassiveUpgradeData
    {
        public FP SlowBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProjectileSlowField>(entity, out var field) == false)
                return;

            field->SpeedMultiplier = FPMath.Max(FP._0, field->SpeedMultiplier - SlowBonus);
        }
    }
}
