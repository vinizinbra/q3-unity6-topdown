namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // A continuously-spinning ground-level beam, dodged by jumping over it rather than by
    // out-ranging or out-positioning it (unlike BeamDeliveryData's single fixed-direction sweep at
    // the locked target) - Enemy.LaserSpinAngle turns at SpinSpeed degrees/sec for the whole
    // Duration, independent of Aim.Angle/the body's own facing, re-firing a box-sweep hit-check
    // (same shape BeamDeliveryData already uses) every TickInterval.
    //
    // Jump-dodge: a target currently airborne (KCC ungrounded) passes clean over the beam - the box
    // overlap query itself has no height awareness of its own (same gap HitEffectUtility.ApplyInShape
    // has), so this checks KCC.Data.IsGrounded per candidate instead of going through that shared
    // helper. Binary (grounded/not), not a height threshold - simpler to reason about and tune than
    // picking an arbitrary "how high is high enough" number, and matches how a real jump over a
    // spinning obstacle reads (safe the instant your feet leave the ground).
    //
    // Always multi-tick (Begin() returns false); Tick() spins until Duration elapses. Pair with an
    // EnemyActionData whose Delivery is NOT chained behind a kinematic step (Leap/Charge) - unlike
    // those, this enemy is expected to stand its ground and spin in place, so it can actually be
    // staggered by knockback mid-spin if InterruptibleDuringActive allows it; no OnInterrupted
    // override is needed for that, though - RotatingLaserVisualManager (View) already stops drawing
    // the moment Enemy.Phase leaves Active for any reason, the same self-healing stop condition
    // RingWaveExpanding's own View manager uses.
    public unsafe class RotatingLaserDeliveryData : EnemyDeliveryData
    {
        // Degrees/sec - positive spins clockwise (viewed from above), same rotation convention
        // FPQuaternion.Euler(0, angle, 0) uses everywhere else in this codebase.
        public FP SpinSpeed = 180;

        public FP Duration = 4;

        // Offset from the enemy's own Aim.Angle at Begin() - 0 starts the sweep pointing wherever
        // the enemy was already facing at windup's end.
        public FP StartAngle = 0;

        public FP BeamLength = 6;
        public FP BeamWidth = 1;
        public FP BeamHeight = FP._1_50;

        // Added on top of the casting enemy's own Transform3D.Y - THE CENTER of the hit-box's
        // vertical extent (box spans [HeightOffset - BeamHeight/2, HeightOffset + BeamHeight/2]),
        // not its bottom - 0 (the default) centers it exactly on the enemy's own pivot height, which
        // for a typical player capsule (pivot at the FEET, collider extending upward) may sit too
        // low to actually overlap it depending on BeamHeight - see the class comment on why this
        // exists. Author this (and/or grow BeamHeight) until the box actually spans the target's
        // real collider. RotatingLaserVisualManager draws its beams at this exact same height
        // (carried via RotatingLaserFired), so the visual line and the real hitbox's center can
        // never drift apart.
        public FP HeightOffset = FP._0;

        // How many beams spin together, evenly spaced around the full circle (e.g. 2 = opposite
        // blades, 4 = an asterisk) - all share the same Enemy.LaserSpinAngle, just offset by
        // 360/BeamCount degrees each, same "one shared angle, N evenly-spaced instances" idiom
        // FanProjectileDeliveryData's own Radial mode uses. Clamped to at least 1.
        public int BeamCount = 1;

        public FP TickInterval = FP._0_25;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->LaserSpinAngle = filter.Aim->Angle + StartAngle;
            filter.Enemy->StateTimer = Duration;

            f.Events.RotatingLaserFired(filter.Entity, BeamLength, BeamWidth, HeightOffset, (byte)System.Math.Max(1, BeamCount));
            FireLaserTick(f, ref filter, action); // first pulse lands immediately, at windup-end
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Void Pressure (Kai) - same reasoning as every other Active-phase Tick in this file
            // family: only ever scales here, never the windup. Scales BOTH the spin rate and the
            // duration countdown, so a slowed laser spins slower in real time too, not just lingers
            // longer at the same speed - see StatusEffectUtility.GetLocalTimeMultiplier's own comment.
            FP timeMultiplier = StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            filter.Enemy->LaserSpinAngle += SpinSpeed * f.DeltaTime * timeMultiplier;

            FP elapsedBefore = Duration - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime * timeMultiplier;
            FP elapsedAfter = Duration - filter.Enemy->StateTimer;

            if (TickInterval > FP._0 && FPMath.Floor(elapsedAfter / TickInterval) != FPMath.Floor(elapsedBefore / TickInterval))
            {
                FireLaserTick(f, ref filter, action);
            }

            return filter.Enemy->StateTimer <= FP._0;
        }

        private void FireLaserTick(Frame f, ref EnemySystem.Filter filter, EnemyActionData action)
        {
            FPVector3 origin = filter.Transform3D->Position + FPVector3.Up * HeightOffset;
            int beamCount = System.Math.Max(1, BeamCount);
            FP beamStep = FP._360 / beamCount;

            // Threaded across every beam/target this whole call - a target standing close enough to
            // the pivot to be caught by more than one beam in the same tick would otherwise produce
            // byte-identical EntityDamaged events (same Target/Damage/Position within one tick),
            // which Quantum silently collapses into a single hit. See AreaHitData.Detonate's own
            // identical comment on HitIndex for the general shape of this problem.
            byte hitIndex = 0;

            for (int beam = 0; beam < beamCount; beam++)
            {
                FP beamAngle = filter.Enemy->LaserSpinAngle + beamStep * beam;
                FPVector3 direction = FPQuaternion.Euler(0, beamAngle, 0) * FPVector3.Forward;

                // origin.Y already IS HeightOffset above the enemy's own pivot - that's the box's
                // CENTER height directly (see HeightOffset's own comment), so no further vertical
                // shift is added here, unlike a ground-anchored delivery (BeamDeliveryData/
                // HitPathSkillAction) that has to lift a ground-level box by half its own height to
                // stop it sitting half underground. RotatingLaserVisualManager's line is drawn at
                // this exact same origin.Y, so the visual and the real hitbox's center always agree.
                FPVector3 center = origin + direction * (BeamLength / 2);

                Shape3D box = Shape3D.CreateBox(new FPVector3(BeamWidth, BeamHeight, BeamLength) / 2);
                FPQuaternion rotation = FPQuaternion.LookRotation(direction, FPVector3.Up);

                var hits = f.Physics3D.OverlapShape(center, rotation, box, -1, QueryOptions.HitAll);

                for (int i = 0; i < hits.Count; i++)
                {
                    EntityRef hitEntity = hits[i].Entity;

                    if (f.Has<PlayerLink>(hitEntity) == false)
                        continue;

                    // Jump-dodge - see class comment. No KCC (shouldn't happen for a real player,
                    // but cheaper to just not exempt it than to assume) means it can't have jumped
                    // clear.
                    if (f.Unsafe.TryGetPointer<KCC>(hitEntity, out var kcc) == true && kcc->Data.IsGrounded == false)
                        continue;

                    if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                        continue;

                    HitEffectContext context = new HitEffectContext
                    {
                        Owner = filter.Entity,
                        Target = hitEntity,
                        Position = hitTransform->Position,
                        PushDirection = direction,
                        Damage = action.Damage,
                        Source = DamageSource.None,
                        Element = ElementType.Neutral,
                        HitIndex = hitIndex++,
                    };

                    HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
                }
            }
        }
    }
}
