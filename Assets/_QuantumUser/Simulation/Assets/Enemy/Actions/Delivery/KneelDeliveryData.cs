namespace Quantum
{
    using Photon.Deterministic;

    // Boss stagger-break payload - meant for BossDataAsset.Stagger.OnBreakForcedAction (see
    // BossSystem.ForceBreakAction). Deals no damage and spawns nothing itself: it just occupies the
    // action slot for Duration (Active phase already blocks normal Idle/Chasing decision-making, so
    // this alone holds the boss still/exposed) while granting extra incoming damage via
    // StatusEffectUtility.ApplyRupture, same bare "expose window" idiom BossComboChainData's
    // combo-finish exposure already uses. One ApplyRupture call in Begin() is enough - its own
    // take-the-stronger/longer reapply semantics mean nothing needs re-applying every tick. The
    // paired EnemyActionData should author Damage = 0, no Effects, and an OnGoingStep (e.g.
    // AttackAnimationType.Crouch) as the only visible "kneeling" tell - no new View code needed.
    public unsafe class KneelDeliveryData : EnemyDeliveryData
    {
        public FP Duration = 3;
        public FP DamageMultiplier = 2;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer = Duration;
            StatusEffectUtility.ApplyRupture(f, filter.Entity, Duration, DamageMultiplier);
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            return filter.Enemy->StateTimer <= FP._0;
        }
    }
}
