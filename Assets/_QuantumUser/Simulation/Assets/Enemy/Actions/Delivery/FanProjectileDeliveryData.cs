namespace Quantum
{
    using Photon.Deterministic;

    // Shotgun-style sibling of ProjectileDeliveryData: fires PelletCount projectiles at once instead
    // of just one. Two spread modes (see Radial):
    // - Cone at the target (Radial = false, the default): each pellet is an independent target point
    //   (the locked target rotated around the origin by its own angle step), so UseArc/LaunchAngle/
    //   Gravity keep working per-pellet exactly like ProjectileDeliveryData's single shot.
    // - Radial burst around self (Radial = true): pellets fan out from the caster's own facing
    //   instead of aiming at SkillTargetPosition at all - needed after a delivery that lands ON the
    //   target (e.g. LeapDeliveryData chained via SequenceDeliveryData), since by then
    //   SkillTargetPosition is colocated with the caster's own new position and a target-relative
    //   cone would solve a zero-length shot for every pellet (see StraightProjectileMovementData -
    //   SolveLaunch bails out and the pellet is silently skipped).
    //
    // No WaitForImpact here - Enemy.SkillProjectile is a single EntityRef, so it can't track more
    // than one in-flight pellet at once. A fan always resolves instantly (Begin() returns true); use
    // plain ProjectileDeliveryData instead for an enemy that needs to stand and watch its shot land.
    public unsafe class FanProjectileDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        // At least 1 - a Count of 1 degenerates to a single shot (straight at the target, or straight
        // ahead if Radial), same as ProjectileDeliveryData but through this class instead.
        public int PelletCount = 5;

        // Cone mode: full cone width in degrees, centered on the target direction - e.g. 30 spreads
        // pellets across -15..+15 either side of dead-on. Radial mode: how much of the full circle to
        // cover around the caster's own facing - 360 (the common case) spreads pellets evenly all the
        // way around with no gap or overlap between the first and last.
        public FP SpreadAngle = 30;

        // False (default): aim each pellet at SkillTargetPosition, cone-spread - the shotgun case.
        // True: ignore SkillTargetPosition entirely and spread pellets around the caster's own
        // Aim.Angle instead - the "burst outward from wherever it's standing" case. See class comment.
        public bool Radial;

        public bool UseArc;

        // Degrees above horizontal each shot leaves at - only meaningful while UseArc is true.
        public FP LaunchAngle = 45;

        // Only meaningful while UseArc is true - see ProjectileDeliveryData.Gravity.
        public FP Gravity = 20;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 targetPosition = filter.Enemy->SkillTargetPosition;
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 resolvedOrigin = ProjectileSpawner.ResolveSpawnOrigin(origin, targetPosition, filter.Aim->Angle, SpawnAnchor, SpawnOffset);

            ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // Same AimsAtTargetCenter reasoning as ProjectileDeliveryData - the cone should
            // converge on the target's body, not its feet. Skipped for Radial (there's no
            // target point at all, see class comment) and UseArc (a lobbed spread still wants
            // to land on the ground around the target, like a single-shot mortar would).
            if (Radial == false && UseArc == false && movement.AimsAtTargetCenter == true &&
                ProjectileAimUtility.TryGetCenterOffset(f, target, out FPVector3 centerOffset) == true)
            {
                targetPosition += centerOffset;
            }

            int pelletCount = PelletCount > 0 ? PelletCount : 1;

            // Radial covers the full requested arc with no double-cover at the seam (step =
            // SpreadAngle / count); a cone instead spans strictly between its two edges (step =
            // SpreadAngle / (count - 1), so a single pellet lands exactly on the first/last edge
            // rather than short of it).
            FP step = Radial == true
                ? SpreadAngle / pelletCount
                : pelletCount > 1 ? SpreadAngle / (pelletCount - 1) : FP._0;
            FP startAngle = Radial == true ? FP._0 : -SpreadAngle / 2;
            FPVector3 baseDirection = FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;
            FPVector3 delta = targetPosition - resolvedOrigin;
            int fired = 0;

            for (int i = 0; i < pelletCount; i++)
            {
                FP angle = startAngle + step * i;

                ProjectileLaunch launch;

                if (Radial == true)
                {
                    // Direction-based, not point-based - there's no meaningful "target point" to
                    // rotate once the caster is already standing where SkillTargetPosition is.
                    FPVector3 pelletDirection = FPQuaternion.Euler(0, angle, 0) * baseDirection;

                    launch = UseArc == true
                        ? ProjectileSpawner.SolveArcLaunch(resolvedOrigin, resolvedOrigin + pelletDirection, LaunchAngle, Gravity)
                        : movement.GetLaunch(f, resolvedOrigin, pelletDirection);
                }
                else
                {
                    // Rotating the whole (not flattened) delta around Y leaves its Y component
                    // alone, so the spread stays flat regardless of the target's elevation - no
                    // separate Y handling needed.
                    FPVector3 pelletTarget = resolvedOrigin + FPQuaternion.Euler(0, angle, 0) * delta;

                    launch = UseArc == true
                        ? ProjectileSpawner.SolveArcLaunch(resolvedOrigin, pelletTarget, LaunchAngle, Gravity)
                        : movement.GetLaunchToTarget(f, resolvedOrigin, pelletTarget, target);
                }

                if (launch.IsValid == false)
                {
                    Log.Error($"[Enemy] {filter.Entity} resolved no valid launch for fan pellet {i} toward {target} - skipped");
                    continue;
                }

                ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, launch, action.Damage, target: target);
                fired++;
            }

            Log.Debug($"[Enemy] {filter.Entity} fired {fired}/{pelletCount} fan pellets ({(Radial ? "radial" : "cone")}) toward {target}");

            return true;
        }
    }
}
