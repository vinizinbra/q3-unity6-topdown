namespace Quantum
{
    using Photon.Deterministic;

    // Single-target pull - drags the target toward self over PullDuration via
    // DamageUtility.ApplyPull (the same continuous-pull primitive VortexSystem uses for its own
    // crowd control), then applies action.Effects once the pull finishes. Always multi-tick
    // (Begin() returns false).
    public unsafe class PullGrabDeliveryData : EnemyDeliveryData
    {
        public FP PullForce = 10;
        public FP PullDuration = FP._0_50;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer = PullDuration;
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer -= f.DeltaTime;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                FPVector3 direction = filter.Transform3D->Position - targetPosition;
                DamageUtility.ApplyPull(f, target, direction, PullForce);
            }

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 finalPosition) == true)
            {
                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = target,
                    Position = finalPosition,
                    PushDirection = filter.Transform3D->Position - finalPosition,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
            }

            return true;
        }
    }
}
