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

        // Gravity is re-applied every tick, so levelling the heading on a pierce would be undone
        // immediately - and the arc is the whole point of this movement. See FlattensOnPierce.
        public override bool FlattensOnPierce => false;

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
            FPVector3 originalTarget = target;
            bool led = false;

            if (PredictionTime > FP._0 && targetEntity != EntityRef.None && f.Unsafe.TryGetPointer<PhysicsBody3D>(targetEntity, out var targetBody) == true)
            {
                // Flattened - a lob aims at the ground the target stands on (AimsAtTargetCenter is
                // false), so a jumping/falling target's own Velocity.Y has no business shifting that
                // ground point. Left un-flattened, it can drive SolveArcLaunch's rise negative for an
                // airborne target (delta.Y skewed by a fall's downward Velocity.Y), silently failing
                // the whole shot (see SolveArcLaunch's own rise <= 0 guard) instead of just leading it.
                FPVector3 flatVelocity = new FPVector3(targetBody->Velocity.X, FP._0, targetBody->Velocity.Z);

                // Clamped to the target's own baseline speed - see ResolveLeadVelocity's own comment.
                // Without this, a knocked-back or erratically-steering (e.g. Flying tier) target's
                // one-tick velocity spike gets extrapolated for the shot's whole flight time, landing
                // the lead point nowhere near where the target will plausibly be.
                flatVelocity = ProjectileAimUtility.ResolveLeadVelocity(f, targetEntity, flatVelocity);

                target = ResolveLeadTarget(spawnPosition, target, flatVelocity, LaunchAngle, Gravity);
                led = true;
            }

            ProjectileLaunch launch = ProjectileSpawner.SolveArcLaunch(spawnPosition, target, LaunchAngle, Gravity);

            // A lead point that's still too extreme (a target right at the edge of the weapon's
            // range, say) can push SolveArcLaunch's own rise <= 0 and fail outright - falls back to
            // the target's real, un-led position instead of WeaponSystem.FireProjectile silently
            // dropping the whole shot.
            if (launch.IsValid == false && led == true)
                launch = ProjectileSpawner.SolveArcLaunch(spawnPosition, originalTarget, LaunchAngle, Gravity);

            return launch;
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

        // A lob's real flight path is an arc, not a straight line, so it always covers more ground
        // than the flat distance it lands at. Left at the base class's 1:1 budget, ProjectileSystem.
        // TryExpire's distance check (Projectile.TraveledDistance, a running sum of true 3D arc
        // length) trips while the shot is still descending - well short of the ground, at whatever
        // height it happened to be at that tick. That's the "grenade explodes in mid-air" bug this
        // fixes, and it hits hardest on a shot fired near the weapon's own max Range, where there's
        // the least slack before the cap.
        //
        // The ratio between arc length and flat range depends only on LaunchAngle - the arc is
        // self-similar, the same shape at any range - so one multiplier covers every shot this weapon
        // ever fires. At the 45-degree default it's ~1.15x; padded well above that here (an exact
        // figure needs an inverse hyperbolic sine, which isn't part of this project's deterministic
        // math surface) so a mis-authored angle still finishes its descent before the budget runs out.
        public override FP ResolveMaxTravelDistance(FP range)
        {
            return range * FP._1_50;
        }
    }
}
