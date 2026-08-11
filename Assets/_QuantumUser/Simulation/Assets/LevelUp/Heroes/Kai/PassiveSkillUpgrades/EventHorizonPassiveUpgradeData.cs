namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Passive Ascension (Event Horizon, line 1/3) - see docs/kai-ascensions.md. Merges the old
    // single-pick EventHorizonPassiveUpgradeData/TimeDilationPassiveUpgradeData/
    // VoidPressurePassiveUpgradeData trio (the latter two deleted) into one line. Rank 1 grows the Void
    // Field's radius; rank 2 ("Time Dilation") additionally slows caught projectiles further; rank 3
    // ("Void Pressure") additionally slows nearby enemies' own attack-execution timers.
    public unsafe partial class EventHorizonPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] RadiusBonus = { FP._1_50, FP.FromString("2.50"), FP.FromString("2.50") };

        // Subtracted from VoidFieldPassiveData.SpeedMultiplier's own baseline (0.60) - {0.10, 0.20,
        // 0.40} gives live projectile speeds of 50%/40%/20% at ranks 1/2/3.
        public FP[] SpeedMultiplierBonus = { FP.FromString("0.10"), FP._0_20, FP.FromString("0.40") };

        // Rank 3 only (0 at ranks 1-2, which leaves ProjectileSlowField.EnemyTimeDilationMultiplier at
        // its 0 "off" default). 0.60 = enemy attack-execution timers run at 60% speed ("40% slower"),
        // same simple-fraction convention the rest of this field already uses (1.0 = normal, lower =
        // slower - see StatusEffectUtility.ApplyTimeDilation/GetLocalTimeMultiplier).
        public FP[] EnemyTimeDilationMultiplier = { FP._0, FP._0, FP.FromString("0.60") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ProjectileSlowField>(entity, out var field) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            // BaseRadius is the immutable spawn-time anchor (see ProjectileSlowField.qtn) - each rank
            // SETS the total (BaseRadius + this rank's bonus), not accumulates on top of whatever a
            // previous rank already added, so a rank 1 -> rank 2 re-pick computes a correct total
            // without needing to know/undo the earlier bonus - same fix Brute's Guardian already
            // applied via ProtectorAura.BaseRadius.
            field->Radius = field->BaseRadius + RadiusBonus[index];
            field->SpeedMultiplier = FPMath.Max(FP._0, field->BaseSpeedMultiplier - SpeedMultiplierBonus[index]);
            field->EnemyTimeDilationMultiplier = EnemyTimeDilationMultiplier[index];
        }
    }
}
