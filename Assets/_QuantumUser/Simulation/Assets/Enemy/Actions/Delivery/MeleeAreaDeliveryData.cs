namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class MeleeAreaDeliveryData : EnemyDeliveryData
    {
        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Re-check range at the moment the action lands - the target may have moved during the windup.
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(filter.Transform3D->Position, targetPosition);
                FP effectiveRange = action.DamageRange + EnemyMovementUtility.ResolveEntityRadius(f, filter.Entity);

                if (sqrDistance <= effectiveRange * effectiveRange)
                {
                    HitEffectContext context = new HitEffectContext
                    {
                        Owner = filter.Entity,
                        Target = target,
                        Position = targetPosition,
                        PushDirection = targetPosition - filter.Transform3D->Position,
                        Damage = action.Damage,
                        Source = DamageSource.None,
                        Element = ElementType.Neutral,
                    };

                    HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
                }
            }

            return true;
        }
    }
}
