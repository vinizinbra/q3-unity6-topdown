namespace Quantum
{
    using Photon.Deterministic;

    // Self-centered radius damage attached to the caster for AuraDuration, ticking every
    // TickInterval - e.g. a damaging pulse around the enemy while it channels. Always multi-tick
    // (Begin() returns false); Tick() channels until AuraDuration elapses. Mirrors
    // HitPathSkillAction.HitAroundCaster's own radial-pulse pattern (the closest existing precedent
    // for a self-centered periodic hit in this codebase).
    public unsafe class AuraDeliveryData : EnemyDeliveryData
    {
        public FP Radius = 3;
        public FP AuraDuration = 2;
        public FP TickInterval = FP._0_50;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer = AuraDuration;
            FireAuraTick(f, ref filter, action); // first pulse lands immediately, at windup-end
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // No spare per-delivery countdown field exists on Enemy, unlike AreaDamageSystem's own
            // dedicated TickTimer - this derives interval-boundary-crossing from the shared
            // StateTimer instead, robust to variable frame delta time (see BeamDeliveryData, same
            // reasoning).
            //
            // Void Pressure (Kai) - see BeamDeliveryData's identical comment on why this decrement is
            // scaled, and why only this Active-phase Tick is ever affected.
            FP elapsedBefore = AuraDuration - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            FP elapsedAfter = AuraDuration - filter.Enemy->StateTimer;

            if (TickInterval > FP._0 && FPMath.Floor(elapsedAfter / TickInterval) != FPMath.Floor(elapsedBefore / TickInterval))
            {
                FireAuraTick(f, ref filter, action);
            }

            return filter.Enemy->StateTimer <= FP._0;
        }

        private void FireAuraTick(Frame f, ref EnemySystem.Filter filter, EnemyActionData action)
        {
            // Lifted by Radius so the sphere sits on the enemy's body rather than half in the
            // floor, same reasoning as HitPathSkillAction.HitAroundCaster.
            FPVector3 center = filter.Transform3D->Position + FPVector3.Up * Radius;
            HitEffectUtility.ApplyInRadius(f, action.Effects, center, Radius, filter.Entity, action.Damage, DamageSource.None);
        }
    }
}
