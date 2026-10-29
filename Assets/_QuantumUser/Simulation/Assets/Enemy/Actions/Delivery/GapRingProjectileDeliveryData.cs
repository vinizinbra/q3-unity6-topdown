namespace Quantum
{
    using Photon.Deterministic;

    // A full ring of projectiles fired outward from the caster with ONE wide opening - e.g. Riff Riot's
    // speaker cabinets (Som no Talo). Not RingSlamDeliveryData: that one is an expanding DAMAGE BAND
    // (an annulus hit-check with no gaps, escaped by outrunning it); this is real projectiles through
    // the normal ProjectileSpawner/ProjectileSystem pipeline (walls stop them, height/collision rules
    // apply), escaped by standing in the opening. Not FanProjectileDeliveryData(Radial) either: a
    // radial fan spreads from the caster's facing with its leftover arc at a fixed spot, while this
    // places the opening on purpose and moves it between casts.
    //
    // The opening is centred on the direction to the target, shifted by a deterministic f.RNG offset
    // in [-GapJitterDegrees, +GapJitterDegrees] - so it changes every emission (same seed = same
    // result on every client) and the player being targeted always has a reachable way out.
    // GapJitterDegrees = 180 makes it fully random. Always instant (Begin() returns true).
    public unsafe class GapRingProjectileDeliveryData : EnemyDeliveryData
    {
        public override bool FiresWeapon => true;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        // Same meaning as FanProjectileDeliveryData's own fields.
        public FP ProjectileSpeedMultiplier = 1;
        public FP ProjectileMaxDistanceMultiplier = 1;

        // Projectiles in the closed part of the ring (the opening holds none).
        public int PelletCount = 18;

        // Width of the opening, in degrees. Pellets spread evenly over the remaining 360 - GapDegrees.
        public FP GapDegrees = 100;

        // How far (either way) the opening may swing off the target direction per emission.
        public FP GapJitterDegrees = 60;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 origin = filter.Transform3D->Position;

            // Flat ring at the targets' body height (see ResolveFlatShotY) - it still flies through the
            // world at that real height, so a ledge/wall between the speaker and a player blocks it.
            origin.Y = ResolveFlatShotY(f, filter.Entity, origin, target);

            FP gapCenter = filter.Aim->Angle;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
            {
                FPVector3 toTarget = targetPosition - filter.Transform3D->Position;

                if (toTarget.X != FP._0 || toTarget.Z != FP._0)
                    gapCenter = FPMath.Atan2(toTarget.X, toTarget.Z) * FP.Rad2Deg;
            }

            if (GapJitterDegrees > FP._0)
                gapCenter += f.RNG->Next(-GapJitterDegrees, GapJitterDegrees);

            ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            int pelletCount = PelletCount > 0 ? PelletCount : 1;
            FP closedArc = FPMath.Clamp(360 - GapDegrees, FP._0, 360);

            // Pellets sit strictly inside the closed arc, half a step in from each edge, so the opening
            // is exactly GapDegrees + one step wide and centred on gapCenter.
            FP step = closedArc / pelletCount;
            FP firstAngle = gapCenter + GapDegrees / 2 + step / 2;
            int fired = 0;

            for (int i = 0; i < pelletCount; i++)
            {
                FP angle = firstAngle + step * i;
                FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;

                ProjectileLaunch launch = movement.GetLaunch(f, origin, direction);
                launch.SpawnPosition = origin;

                if (launch.IsValid == false)
                    continue;

                EntityRef pellet = ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, ref launch, action.Damage, target: target,
                    speedMultiplier: ProjectileSpeedMultiplier);

                if (f.Unsafe.TryGetPointer<Projectile>(pellet, out var spawned) == true)
                    spawned->MaxDistanceMultiplier = ProjectileMaxDistanceMultiplier;

                fired++;
            }

            Log.Debug($"[Enemy] {filter.Entity} fired a {fired}-pellet ring, opening {GapDegrees} deg at {gapCenter}");
            return true;
        }
    }
}
