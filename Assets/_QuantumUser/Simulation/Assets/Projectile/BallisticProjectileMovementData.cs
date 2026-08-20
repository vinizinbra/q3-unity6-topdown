namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // A mortar lob: LaunchAngle and Gravity are the authored constants and speed is what's derived,
    // so apex and flight time both scale with range and the arc keeps one silhouette that simply
    // grows with distance. Deriving gravity per shot instead - the shape this had before - swung it
    // ~100x across normal range, which reads as near shots snapping and far ones floating.
    public unsafe class BallisticProjectileMovementData : ProjectileMovementData
    {
        public FP LaunchAngle = 45;
        public FP Gravity = 20;

        // Only reached by shots with no point to land on - a free-aimed weapon, a cluster bomblet.
        // Anything targeted solves onto the real target instead.
        public FP TargetDistance = 10;

        // Leads a moving target - any positive value turns leading on (0, the default, aims at the
        // target's exact position, same as before this existed). Only entities with a PhysicsBody3D
        // produce a velocity to lead with - that's every enemy, but not the KCC-driven player, so a
        // shot aimed at a player never leads.
        //
        // NOT read as a literal number of seconds - see ResolveLeadTarget's own comment for why a
        // fixed lead time can't work here (the arc's own flight time scales with sqrt(distance), so
        // one authored constant is only ever correct at a single specific range - this was the exact
        // bug behind "ballistic shots keep missing", worse the farther they travel). Kept as an FP
        // rather than a bool purely so existing authored values (e.g. 0.2) stay > 0 and keep leading
        // enabled without needing every asset re-authored.
        public FP PredictionTime = 0;

        // A lob descends onto the ground the target stands on, not into its chest.
        public override bool AimsAtTargetCenter => false;

        protected override FPVector3 GetTargetPoint(FPVector3 origin, FPVector3 direction)
        {
            return GetFlatTargetPoint(origin, direction, TargetDistance);
        }

        // Same reasoning as ThrownProjectileMovementData's own override - a mortar lob is an arc under
        // constant gravity, so scaling the whole solved vector would multiply its range by the square
        // of the multiplier and drop the shell well past the point SolveLaunch just aimed it at. This
        // deliberately flattens the arc below the authored LaunchAngle instead: the whole point of a
        // speed bonus on a lob is that it arrives sooner, and a solved shot that no longer lands on
        // its solution is simply broken.
        public override void ApplySpeedMultiplier(ref ProjectileLaunch launch, FP multiplier)
        {
            if (multiplier <= FP._0 || multiplier == FP._1)
                return;

            ScaleArcPreservingRange(ref launch, multiplier);
        }

        protected override ProjectileLaunch SolveLaunch(Frame f, FPVector3 spawnPosition, FPVector3 target, EntityRef targetEntity)
        {
            if (PredictionTime > FP._0 && targetEntity != EntityRef.None && f.Unsafe.TryGetPointer<PhysicsBody3D>(targetEntity, out var targetBody) == true)
            {
                // Flattened - a lob aims at the ground the target stands on (AimsAtTargetCenter is
                // false), so a jumping/falling target's own Velocity.Y has no business shifting that
                // ground point. Left un-flattened, it can drive SolveArcLaunch's rise negative for an
                // airborne target (delta.Y skewed by a fall's downward Velocity.Y), silently failing
                // the whole shot (see SolveArcLaunch's own rise <= 0 guard) instead of just leading it.
                FPVector3 flatVelocity = new FPVector3(targetBody->Velocity.X, FP._0, targetBody->Velocity.Z);
                target = ResolveLeadTarget(spawnPosition, target, flatVelocity, LaunchAngle, Gravity);
            }

            return ProjectileSpawner.SolveArcLaunch(spawnPosition, target, LaunchAngle, Gravity);
        }

        // SolveArcLaunch's own derivation makes flightTime == sqrt(2 * rise / gravity) - it grows with
        // distance, it's never constant. Leading by one fixed guess (the old PredictionTime-as-seconds
        // behavior) is only ever correct at the one range it happened to be tuned for - too little
        // lead at long range (the shot lands behind a moving target), too much up close. Refines the
        // lead against the arc's own actual flight time instead: solve the arc onto the current guess,
        // read back how long that specific arc would really take, and re-lead by that - converges in a
        // few passes since a target's own movement speed is always far slower than a projectile's, and
        // a fixed iteration count keeps this deterministic (no while-loop convergence check needed).
        private static FPVector3 ResolveLeadTarget(FPVector3 spawnPosition, FPVector3 target, FPVector3 targetVelocity, FP launchAngle, FP gravity)
        {
            FPVector3 leadTarget = target;

            for (int i = 0; i < 3; i++)
            {
                ProjectileLaunch trial = ProjectileSpawner.SolveArcLaunch(spawnPosition, leadTarget, launchAngle, gravity);

                if (trial.IsValid == false)
                    break;

                FPVector3 flatVelocity = new FPVector3(trial.Velocity.X, FP._0, trial.Velocity.Z);
                FP flatSpeed = flatVelocity.Magnitude;

                if (flatSpeed <= FP._0)
                    break;

                FPVector3 flatDelta = new FPVector3(leadTarget.X - spawnPosition.X, FP._0, leadTarget.Z - spawnPosition.Z);
                FP flightTime = flatDelta.Magnitude / flatSpeed;

                leadTarget = target + targetVelocity * flightTime;
            }

            return leadTarget;
        }

        public override void UpdateVelocity(Frame f, FPVector3 position, Projectile* projectile)
        {
            projectile->Velocity.Y -= Gravity * f.DeltaTime;
        }
    }
}
