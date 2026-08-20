namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Event Horizon, line 1/3) - see docs/kai-ascensions.md. Merges the old
    // single-pick EventHorizonPassiveUpgradeData/TimeDilationPassiveUpgradeData/
    // VoidPressurePassiveUpgradeData trio (the latter two deleted) into one line. Rank 1 grows the Void
    // Field's radius; rank 2 ("Time Dilation") additionally slows caught projectiles further; rank 3
    // ("Void Pressure") additionally slows nearby enemies' own attack-execution timers.
    public unsafe partial class EventHorizonPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Absolute Void Field radius per rank, not a bonus - each rank SETS the total. Floored at the field's own spawn-time BaseRadius so this can never shrink it.")]
        public FP[] Radius = { 4, 5, 5 };

        [Tooltip("Live projectile speed inside the field, per rank - 0.65 = enemy projectiles fly at 65% speed (-35%). Deliberately far milder than the pre-rebalance 0.20 (-80%), which suppressed enemy ranged pressure almost entirely.")]
        public FP[] ProjectileSpeedMultiplier = { FP.FromString("0.65"), FP._0_50, FP.FromString("0.40") };

        [Tooltip("Rank 3 only - enemy attack-execution timers inside the field. 0.80 = 20% slower. 0 leaves it off.")]
        public FP[] EnemyTimeDilationMultiplier = { FP._0, FP._0, FP.FromString("0.80") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ProjectileSlowField>(entity, out var field) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            // Each rank SETS the total rather than accumulating, so a rank 1 -> rank 2 re-pick lands
            // on a correct value without needing to know/undo whatever the earlier rank added - same
            // fix Brute's Guardian already applied via ProtectorAura.BaseRadius. BaseRadius/
            // BaseSpeedMultiplier remain the immutable spawn-time anchors (see
            // ProjectileSlowField.qtn); the radius floor keeps a low authored rank from ever
            // shrinking the base field.
            field->Radius = FPMath.Max(field->BaseRadius, Radius[index]);
            field->SpeedMultiplier = FPMath.Clamp(ProjectileSpeedMultiplier[index], FP._0, field->BaseSpeedMultiplier);
            field->EnemyTimeDilationMultiplier = EnemyTimeDilationMultiplier[index];
        }
    }
}
