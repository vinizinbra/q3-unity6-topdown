namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Continuous line-shaped damage for BeamDuration, ticking every TickInterval - e.g. a laser
    // sweep along the locked aim direction (Enemy.SkillTargetPosition, captured at windup-commit).
    // Always multi-tick (Begin() returns false); Tick() channels until BeamDuration elapses.
    // Mirrors HitPathSkillAction's own directional box-sweep shape/lift math (the closest existing
    // precedent for a directional area hit in this codebase).
    public unsafe class BeamDeliveryData : EnemyDeliveryData
    {
        public FP BeamLength = 8;
        public FP BeamWidth = 1;
        public FP BeamHeight = 1;
        public FP BeamDuration = 1;
        public FP TickInterval = FP._0_25;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer = BeamDuration;
            FireBeamTick(f, ref filter, action); // first pulse lands immediately, at windup-end
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // No spare per-delivery countdown field exists on Enemy, unlike AreaDamageSystem's own
            // dedicated TickTimer - this derives interval-boundary-crossing from the shared
            // StateTimer instead, robust to variable frame delta time.
            FP elapsedBefore = BeamDuration - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime;
            FP elapsedAfter = BeamDuration - filter.Enemy->StateTimer;

            if (TickInterval > FP._0 && FPMath.Floor(elapsedAfter / TickInterval) != FPMath.Floor(elapsedBefore / TickInterval))
            {
                FireBeamTick(f, ref filter, action);
            }

            return filter.Enemy->StateTimer <= FP._0;
        }

        private void FireBeamTick(Frame f, ref EnemySystem.Filter filter, EnemyActionData action)
        {
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 delta = filter.Enemy->SkillTargetPosition - origin;
            FPVector3 flatDelta = new FPVector3(delta.X, FP._0, delta.Z);

            if (flatDelta.SqrMagnitude <= FP._0)
                return;

            FP length = FPMath.Min(flatDelta.Magnitude, BeamLength);
            FPVector3 direction = flatDelta.Normalized;

            // Lifted to body height, same reasoning as HitPathSkillAction - a box centered on the
            // ground plane sits half underground and catches the floor instead of what it swept.
            FPVector3 center = origin + direction * (length / 2) + FPVector3.Up * (BeamHeight / 2);

            // Shape3D box extents are half-sizes, and LookRotation puts the length on Z.
            Shape3D box = Shape3D.CreateBox(new FPVector3(BeamWidth, BeamHeight, length) / 2);
            FPQuaternion rotation = FPQuaternion.LookRotation(direction, FPVector3.Up);

            HitEffectUtility.ApplyInShape(f, action.Effects, center, rotation, box, filter.Entity, action.Damage, DamageSource.None, direction);
        }
    }
}
