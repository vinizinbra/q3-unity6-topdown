namespace Quantum
{
    using Photon.Deterministic;

    // A rotating pattern of alternating danger/safe pie-slice wedges around the caster, e.g.
    // Scrapjaw's Scrapstorm ultimate - always self-centered (frozen at Begin(), independent of
    // action.Origin, same "this delivery doesn't check Origin" precedent AuraDeliveryData already
    // sets for a self-centered effect). DangerSectorCount evenly-spaced danger wedges alternate with
    // an equal number of same-width safe wedges, all spinning together at RotationDegreesPerSecond.
    // The View's own paired TelegraphData.RotationDegreesPerSecond must be authored to match this
    // field by hand - there's no single source of truth linking sim rotation speed to what's shown,
    // unlike a Circle telegraph's radius (always action.DamageRange x RadiusMultiplier). Always
    // multi-tick (Begin() returns false); Tick() channels until Duration elapses, then applies the
    // Exposed window via StatusEffectUtility.ApplyRupture (Boss-tier-safe, unlike Stun - see
    // ChargeDeliveryData.WallStunDuration's own note). Mirrors AuraDeliveryData/BeamDeliveryData's
    // own multi-tick shape (StateTimer-driven, Void-Pressure-scaled decrement, TickInterval
    // boundary-crossing).
    public unsafe class ScrapstormDeliveryData : EnemyDeliveryData
    {
        public FP Duration = 7;

        // Must match the paired TelegraphData.RotationDegreesPerSecond by hand - see class comment.
        public FP RotationDegreesPerSecond = 45;

        // Danger wedges alternate with an equal number of same-width safe wedges - e.g. 4 here means
        // 4 danger + 4 safe wedges, each 360/8 = 45 degrees wide.
        public int DangerSectorCount = 4;

        public FP TickInterval = FP._0_25;

        // Applied once, when Duration elapses - 0 (default) opts out.
        public FP ExposedDurationOnFinish;
        public FP ExposedDamageMultiplierOnFinish = FP._1_50;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->SkillStartPosition = filter.Transform3D->Position;
            filter.Enemy->StateTimer = Duration;
            FireStormTick(f, ref filter, action); // first pulse lands immediately, at windup-end
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Void Pressure (Kai) - same reasoning as AuraDeliveryData/BeamDeliveryData's identical
            // comment: only this Active-phase Tick is ever scaled, never the windup/telegraph.
            FP elapsedBefore = Duration - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            FP elapsedAfter = Duration - filter.Enemy->StateTimer;

            if (TickInterval > FP._0 && FPMath.Floor(elapsedAfter / TickInterval) != FPMath.Floor(elapsedBefore / TickInterval))
            {
                FireStormTick(f, ref filter, action);
            }

            bool finished = filter.Enemy->StateTimer <= FP._0;

            if (finished == true && ExposedDurationOnFinish > FP._0)
            {
                StatusEffectUtility.ApplyRupture(f, filter.Entity, ExposedDurationOnFinish, ExposedDamageMultiplierOnFinish);
            }

            return finished;
        }

        private void FireStormTick(Frame f, ref EnemySystem.Filter filter, EnemyActionData action)
        {
            if (DangerSectorCount <= 0)
                return;

            FPVector3 center = filter.Enemy->SkillStartPosition;

            // Same formula the paired View telegraph reconstructs independently off enemy.StateTimer
            // (EnemyAttackVisualsView.ComputeTelegraphPose) - deliberately derived from StateTimer
            // rather than a separately-tracked elapsed-time field, so sim and view can never desync
            // from double-counting/rounding two independent clocks.
            FP rotationAngle = -filter.Enemy->StateTimer * RotationDegreesPerSecond;

            FP fullCircle = 360;

            // Spacing between consecutive danger-wedge CENTERS - one danger wedge + one equal-width
            // safe wedge per step, so N danger wedges land evenly around the full circle.
            FP sectorStep = fullCircle / DangerSectorCount;

            // A single danger wedge's own angular WIDTH is only half of sectorStep (the other half
            // is the safe gap before the next danger wedge starts) - same dot-vs-cosine idiom
            // GroundAreaDeliveryData.ConeShaped already uses for its own angular check, deliberately
            // not Atan2 (cheaper, stays consistent with this codebase's one existing convention).
            FP wedgeWidth = fullCircle / (DangerSectorCount * 2);
            FP halfWidthCos = FPMath.Cos(wedgeWidth * FP._0_50 * FP.Deg2Rad);

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, center, action.DamageRange);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FPVector3 hitPosition = hitTransform->Position;
                FPVector3 toHit = new FPVector3(hitPosition.X - center.X, FP._0, hitPosition.Z - center.Z);

                if (toHit.SqrMagnitude <= FP._0)
                    continue; // standing exactly on the center - no meaningful direction to angle-check

                FPVector3 toHitDirection = toHit.Normalized;
                bool inDanger = false;

                for (int sector = 0; sector < DangerSectorCount; sector++)
                {
                    FP sectorAngle = rotationAngle + sectorStep * sector;
                    FPVector3 sectorDirection = FPQuaternion.Euler(0, sectorAngle, 0) * FPVector3.Forward;

                    if (FPVector3.Dot(sectorDirection, toHitDirection) >= halfWidthCos)
                    {
                        inDanger = true;
                        break;
                    }
                }

                if (inDanger == false)
                    continue;

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = toHit,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }
        }
    }
}
