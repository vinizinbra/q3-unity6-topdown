namespace Quantum
{
    using Photon.Deterministic;

    // Holds the caster in place for Duration and does nothing else - no damage, no exposure. Exists for
    // purely presentational beats driven by the sim, e.g. a boss's phase-entry roar
    // (BossPhaseData.EntryAction): the action's OnGoingStep shows the pose, and because it is a real
    // Active phase, normal decision-making is held off for exactly as long as the pose plays. Same
    // shape as KneelDeliveryData minus its ApplyRupture.
    public unsafe class HoldPoseDeliveryData : EnemyDeliveryData
    {
        public FP Duration = FP._1;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer = Duration;
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            return filter.Enemy->StateTimer <= FP._0;
        }
    }
}
