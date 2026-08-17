namespace Quantum
{
    using Photon.Deterministic;

    // A big central slam plus an outward-expanding second wave - e.g. Scrapjaw's Crusher Slam.
    // Begin() applies the instant central hit; Tick() then sweeps a thin damage band outward from
    // the same center until it reaches action.DamageRange (the SAME field the paired Circle
    // telegraph already shows at full size up front via TelegraphData.RadiusMultiplier - never
    // authored separately here, so the ring's outer reach and what the telegraph shows can't drift
    // apart). Escaping is just outrunning the front: a player whose distance from center stays ahead
    // of the current front radius is never caught, and a stationary player is hit exactly once, the
    // tick the front sweeps past them. Always multi-tick (Begin() returns false); Tick() channels
    // until RingDuration elapses or the front reaches DamageRange. Mirrors AuraDeliveryData/
    // BeamDeliveryData's own multi-tick shape (StateTimer-driven, Void-Pressure-scaled decrement).
    public unsafe class RingSlamDeliveryData : EnemyDeliveryData
    {
        public FP InnerBlastRadius = 2;

        // Ring tick damage = action.Damage * this - lets the outward wave hit softer than the
        // initial slam without needing a second Damage field on the outer action.
        public FP RingDamagePercent = FP._0_50;

        public FP RingExpansionSpeed = 6;
        public FP RingDuration = 1;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 center = filter.Transform3D->Position;

            HitEffectUtility.ApplyInRadius(f, action.Effects, center, InnerBlastRadius, filter.Entity, action.Damage, DamageSource.None);

            // Repurposes SkillStartPosition the same way LeapDeliveryData already does for its own
            // in-flight arc state - Begin() is the only place this delivery ever writes it, and
            // Tick() reads it back every tick as the ring's frozen center.
            filter.Enemy->SkillStartPosition = center;
            filter.Enemy->StateTimer = RingDuration;
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Void Pressure (Kai) - same reasoning as AuraDeliveryData/BeamDeliveryData's identical
            // comment: only this Active-phase Tick is ever scaled, never the windup/telegraph.
            FP elapsedBefore = RingDuration - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            FP elapsedAfter = RingDuration - filter.Enemy->StateTimer;

            FP outerReach = action.DamageRange;
            FP previousFront = FPMath.Clamp(elapsedBefore * RingExpansionSpeed, FP._0, outerReach);
            FP currentFront = FPMath.Clamp(elapsedAfter * RingExpansionSpeed, FP._0, outerReach);

            if (currentFront > previousFront)
            {
                FireRingBand(f, ref filter, action, previousFront, currentFront);
            }

            return filter.Enemy->StateTimer <= FP._0 || currentFront >= outerReach;
        }

        // Damages only players whose flat distance from center falls in [innerBound, outerBound) -
        // the annulus the front swept through THIS tick. previousFront only ever grows tick over
        // tick, so once it passes a stationary player they can never satisfy this check again.
        private void FireRingBand(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, FP innerBound, FP outerBound)
        {
            FPVector3 center = filter.Enemy->SkillStartPosition;
            var hits = EnemyMovementUtility.FindPlayersInRadius(f, center, outerBound);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FPVector3 hitPosition = hitTransform->Position;
                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(center, hitPosition);

                if (sqrDistance < innerBound * innerBound)
                    continue; // front hasn't reached them yet this tick (or already swept past earlier)

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = hitPosition - center,
                    Damage = action.Damage * RingDamagePercent,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }
        }
    }
}
