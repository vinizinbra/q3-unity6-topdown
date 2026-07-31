namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - enemies standing in the Void Field (Filler/Normal/Specialist only, never
    // Elite/Boss - see VoidFieldSystem.ApplyFieldsToEnemies) get their own attack-execution timers
    // slowed, not their movement speed: a true time dilation on their action clock (e.g. a Leap's
    // whole jump arc plays out slower in real time), not a movement-speed nerf. Only ever applies to
    // Kai's own continuous field, not a dropped SlowArea instance - see ProjectileSlowField.qtn's own
    // comment on EnemyTimeDilationMultiplier.
    public unsafe partial class VoidPressurePassiveUpgradeData : PassiveUpgradeData
    {
        public FP EnemyTimeDilationMultiplier = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProjectileSlowField>(entity, out var field) == false)
                return;

            field->EnemyTimeDilationMultiplier = EnemyTimeDilationMultiplier;
        }
    }
}
