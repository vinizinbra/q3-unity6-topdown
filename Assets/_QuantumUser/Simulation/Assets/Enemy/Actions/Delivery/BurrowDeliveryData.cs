namespace Quantum
{
    using Photon.Deterministic;

    // Dives underground near its current spot, travels invisibly to a new point (scattered around
    // the target via the base class's RandomizeAroundAnchor), then resurfaces there - the enemy is
    // Invulnerable + Burrowed for the whole Active phase, so DamageUtility ignores every hit and
    // AimSystem/VortexSystem/EnemyMovementUtility.TryFindNearestEnemy all skip it as a target (see
    // their own Invulnerable checks). No damage Effects - this is pure repositioning, same as
    // TeleportBlinkDeliveryData; whatever action the enemy commits to after resurfacing (via the
    // normal Recovery -> Chasing -> Preparation cycle) is its own separately-telegraphed, avoidable
    // attack, not something this delivery bakes in itself.
    //
    // Pair with an EnemyActionData authored with a large EngageRange (TrySelectAction's range gate
    // can't be bypassed by Trigger alone - see EnemyDecisionUtility.cs), a long CooldownTime (so it
    // can't burrow back-to-back), and optionally Trigger.Type = OnHealthThreshold so it reads as an
    // escape rather than a random reposition.
    public unsafe class BurrowDeliveryData : EnemyDeliveryData
    {
        public FP DiveDuration = FP._0_50;
        public FP TravelDuration = FP._1;
        public FP ResurfaceDuration = FP._0_50;

        // How far below its own ground level the enemy sinks while Traveling - purely a visual/feel
        // parameter (the whole point is it's not visible then), doesn't affect where it lands.
        public FP DiveDepth = 2;

        private FP TotalDuration => DiveDuration + TravelDuration + ResurfaceDuration;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // SkillTargetPosition is already the target's raw position (established by
            // OnAnticipating/UpdateChasing) - scatter it via the base class's own randomization
            // instead of resolving straight onto the target, same as ScatterDeliveryData.
            FPVector3 destination = RandomizeAroundAnchor(f, filter.Enemy->SkillTargetPosition);

            filter.Enemy->SkillStartPosition = filter.Transform3D->Position;

            // Ground-corrects the destination the same way LeapDeliveryData.Begin does for its
            // landing spot - falls back to the raw scattered point (still flattened to the
            // enemy's own current Y below) if no ground is found there, rather than failing the
            // whole delivery outright.
            if (data.Stats.Height.InitialState == EnemyHeightState.Grounded &&
                EnemyMovementUtility.TryFindGroundHeight(f, destination, EnemyMovementUtility.GetGroundLayerMask(f), out FP destinationGroundY) == true)
            {
                destination.Y = destinationGroundY;
            }
            else
            {
                destination.Y = filter.Enemy->SkillStartPosition.Y;
            }

            filter.Enemy->SkillTargetPosition = destination;
            filter.Enemy->StateTimer = TotalDuration;
            filter.PhysicsBody3D->IsKinematic = true;

            f.Add<Invulnerable>(filter.Entity);
            f.Add<Burrowed>(filter.Entity);

            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Same Void Pressure (Kai) time-dilation reasoning as LeapDeliveryData.Tick - only the
            // Active phase is stretched, not the windup.
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            FP elapsed = FPMath.Clamp(TotalDuration - filter.Enemy->StateTimer, FP._0, TotalDuration);
            FPVector3 start = filter.Enemy->SkillStartPosition;
            FPVector3 destination = filter.Enemy->SkillTargetPosition;

            if (elapsed < DiveDuration)
            {
                // Diving - sinks straight down in place, hasn't started traveling yet.
                FP t = DiveDuration > FP._0 ? FPMath.Clamp01(elapsed / DiveDuration) : FP._1;
                filter.Transform3D->Position = new FPVector3(start.X, start.Y - DiveDepth * t, start.Z);
            }
            else if (elapsed < DiveDuration + TravelDuration)
            {
                // Traveling underground - fully sunk, moving from the takeoff spot to the resolved
                // destination. Y is held at each point's own -DiveDepth rather than lerped, so a
                // takeoff/landing height difference doesn't read as tunneling at an angle (it's
                // invisible anyway, but Resurface below still needs a clean start point).
                FP t = TravelDuration > FP._0 ? FPMath.Clamp01((elapsed - DiveDuration) / TravelDuration) : FP._1;
                FPVector3 flat = FPVector3.Lerp(start, destination, t);
                FP depth = FPMath.Lerp(start.Y - DiveDepth, destination.Y - DiveDepth, t);
                filter.Transform3D->Position = new FPVector3(flat.X, depth, flat.Z);
            }
            else
            {
                // Resurfacing - already at destination XZ, rising from -DiveDepth back to real
                // ground level.
                FP t = ResurfaceDuration > FP._0
                    ? FPMath.Clamp01((elapsed - DiveDuration - TravelDuration) / ResurfaceDuration)
                    : FP._1;
                filter.Transform3D->Position = new FPVector3(destination.X, destination.Y - DiveDepth * (FP._1 - t), destination.Z);
            }

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            filter.Transform3D->Position = destination;

            f.Remove<Invulnerable>(filter.Entity);
            f.Remove<Burrowed>(filter.Entity);

            return true;
        }
    }
}
