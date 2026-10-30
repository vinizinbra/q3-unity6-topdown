namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Runs a boss's crowd barrage (CrowdBarrage, started by CrowdMarkEffectData) and ages CrowdMarks.
    //
    // Each salvo picks ONE marked, living player (round-robin by entity index, so co-op alternates instead of
    // multiplying) and throws ThrowsPerSalvo pieces of trash at them, ThrowInterval apart, within a hard total budget.
    // Every throw samples its landing point at throw time (target's spot, then a ring around it), launches a real
    // ballistic projectile from OUTSIDE the arena (CrowdMarkEffectData's crowd origin) and fires the landing warning
    // for that exact point and flight time - committed, no homing, so a slowed player can still walk out. Only
    // throws actually made get a warning, so cancelling the barrage leaves nothing stale behind; trash already in the
    // air finishes its warned flight.
    [Preserve]
    public unsafe class BossCrowdBarrageSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            TickMarks(f);

            List<EntityRef> finished = null;

            var barrages = f.Filter<CrowdBarrage>();
            while (barrages.NextUnsafe(out EntityRef boss, out CrowdBarrage* barrage))
            {
                if (TickBarrage(f, boss, barrage) == false)
                    (finished ??= new List<EntityRef>()).Add(boss);
            }

            if (finished == null)
                return;

            for (int i = 0; i < finished.Count; i++)
                f.Remove<CrowdBarrage>(finished[i]);
        }

        private static void TickMarks(Frame f)
        {
            List<EntityRef> expired = null;

            var marks = f.Filter<CrowdMark>();
            while (marks.NextUnsafe(out EntityRef player, out CrowdMark* mark))
            {
                mark->Remaining -= f.DeltaTime;

                if (mark->Remaining <= FP._0 || BossHazardUtility.IsOwnerAlive(f, mark->Boss) == false || PlayerLifeStateUtility.IsIncapacitated(f, player) == true)
                    (expired ??= new List<EntityRef>()).Add(player);
            }

            if (expired == null)
                return;

            for (int i = 0; i < expired.Count; i++)
                f.Remove<CrowdMark>(expired[i]);
        }

        // Returns false once the barrage is over (caller removes the component after the filter loop).
        private static bool TickBarrage(Frame f, EntityRef boss, CrowdBarrage* barrage)
        {
            CrowdMarkEffectData config = f.FindAsset(barrage->Config);

            if (config == null || BossHazardUtility.IsOwnerAlive(f, boss) == false)
                return false;

            barrage->Timer -= f.DeltaTime;
            if (barrage->Timer > FP._0)
                return true;

            bool firstOfSalvo = false;

            if (barrage->ThrowsLeftInSalvo == 0)
            {
                if (barrage->SalvosRemaining == 0 || barrage->ThrowsRemaining <= 0)
                    return false;

                if (TryPickNextTarget(f, boss, barrage, out EntityRef target) == false)
                    return false; // nobody marked and alive any more

                barrage->SalvoTarget = target;
                barrage->SalvosRemaining--;
                barrage->ThrowsLeftInSalvo = (byte)System.Math.Min(config.ThrowsPerSalvo, barrage->ThrowsRemaining);
                barrage->Timer = FP._0;
                firstOfSalvo = true;
            }

            if (IsValidTarget(f, boss, barrage->SalvoTarget) == false)
            {
                // Target went down/left mid-salvo: drop the rest of this salvo, the next one re-picks.
                barrage->ThrowsLeftInSalvo = 0;
                barrage->Timer = config.SalvoInterval;
                return true;
            }

            Throw(f, boss, barrage, config, firstOfSalvo);

            barrage->ThrowsLeftInSalvo--;
            barrage->ThrowsRemaining--;
            barrage->ThrowCounter++;
            barrage->Timer = barrage->ThrowsLeftInSalvo > 0 ? config.ThrowInterval : config.SalvoInterval;
            return true;
        }

        private static bool IsValidTarget(Frame f, EntityRef boss, EntityRef player)
        {
            return player != EntityRef.None
                && f.Exists(player)
                && f.Has<PlayerLink>(player)
                && PlayerLifeStateUtility.IsIncapacitated(f, player) == false
                && f.Unsafe.TryGetPointer<CrowdMark>(player, out var mark) == true
                && mark->Boss == boss;
        }

        // Next marked player after the last one targeted, by entity index (wrapping) - deterministic round-robin.
        private static bool TryPickNextTarget(Frame f, EntityRef boss, CrowdBarrage* barrage, out EntityRef target)
        {
            target = EntityRef.None;
            EntityRef lowest = EntityRef.None;
            EntityRef nextAfter = EntityRef.None;

            var marks = f.Filter<CrowdMark>();
            while (marks.NextUnsafe(out EntityRef player, out CrowdMark* _))
            {
                if (IsValidTarget(f, boss, player) == false)
                    continue;

                if (lowest == EntityRef.None || player.Index < lowest.Index)
                    lowest = player;

                if (player.Index > barrage->LastTargetIndex && (nextAfter == EntityRef.None || player.Index < nextAfter.Index))
                    nextAfter = player;
            }

            target = nextAfter != EntityRef.None ? nextAfter : lowest;
            if (target == EntityRef.None)
                return false;

            barrage->LastTargetIndex = target.Index;
            return true;
        }

        private static void Throw(Frame f, EntityRef boss, CrowdBarrage* barrage, CrowdMarkEffectData config, bool firstOfSalvo)
        {
            if (config.TrashProjectiles.Count == 0 || f.Unsafe.TryGetPointer<Transform3D>(barrage->SalvoTarget, out var targetTransform) == false)
                return;

            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 anchor = targetTransform->Position;

            AssetRef<ProjectileDataAsset> projectileRef = config.TrashProjectiles[barrage->ThrowCounter % config.TrashProjectiles.Count];
            ProjectileDataAsset projectileData = f.FindAsset(projectileRef);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // Lead off a first solve to the target's spot; the crowd point is picked from the led anchor below.
            if (config.LeadFactor > FP._0)
            {
                FPVector3 aimOrigin = ResolveCrowdOrigin(f, anchor, config);
                ProjectileLaunch aim = movement.GetLaunchToTarget(f, aimOrigin, anchor, EntityRef.None);

                if (aim.IsValid == true)
                {
                    DifficultyUtility.ScaleEnemyLead(f, config.LeadFactor, config.MaxLeadDistance, out FP lead, out FP maxLead);
                    FP shotSpeed = DifficultyUtility.ResolveEnemyShotSpeed(f, boss, FP._0);
                    anchor = ProjectileAimUtility.LeadAimPoint(f, barrage->SalvoTarget, aimOrigin, anchor, aim.Velocity * shotSpeed, lead, maxLead);
                }
            }

            FPVector3 landing = anchor;

            if (firstOfSalvo == false)
                landing = EnemyMovementUtility.RandomPositionInRing(f, anchor, config.LandingSpreadMin, config.LandingSpreadMax);

            if (EnemyMovementUtility.TryFindGroundHeight(f, landing, groundMask, out FP landingGroundY) == false)
            {
                landing = targetTransform->Position; // led/ring point over a void - drop it on the target's own floor instead
                landingGroundY = EnemyMovementUtility.ResolveGroundY(f, landing);
            }

            landing.Y = landingGroundY;

            FPVector3 origin = ResolveCrowdOrigin(f, landing, config);

            ProjectileLaunch launch = movement.GetLaunchToTarget(f, origin, landing, EntityRef.None);
            launch.SpawnPosition = origin;

            if (launch.IsValid == false)
                return;

            EntityRef thrown = ProjectileSpawner.Spawn(f, boss, projectileRef, ref launch, config.ThrowDamage);

            FP radius = projectileData.Hit.IsValid == true && f.FindAsset(projectileData.Hit) is AreaHitData areaHit ? areaHit.BlastRadius : FP._0;
            FPVector3 flatDelta = new FPVector3(landing.X - origin.X, FP._0, landing.Z - origin.Z);
            FPVector3 flatVelocity = new FPVector3(launch.Velocity.X, FP._0, launch.Velocity.Z);
            FP flightTime = flatVelocity.Magnitude > FP._0 ? flatDelta.Magnitude / flatVelocity.Magnitude : FP._0;

            // Thrown from outside the arena, so the arc crosses its walls/gates: the trash's hit ignores level geometry
            // and instead expires exactly when the arc reaches the warned point - its AreaHit detonates on expiry, right
            // there. (Players in the way still get hit - enemy shots always detonate on players.)
            if (flightTime > FP._0 && f.Unsafe.TryGetPointer<Projectile>(thrown, out var projectile) == true)
                projectile->RemainingLifetime = flightTime;

            // Warning on the real landing point for the real flight time; Owner None so it isn't dropped when the boss
            // moves on to its next action.

            if (radius > FP._0 && flightTime > FP._0)
                f.Events.ProjectileLandingWarning(landing, flightTime, radius, EntityRef.None);
        }

        // A crowd point OUTSIDE the boss arena: the arena chunk's perimeter, pushed OutsideMargin out, raised ThrowHeight
        // above the landing floor. Samples a few random perimeter points (deterministic f.RNG) and keeps the first within
        // MaxThrowDistance, else the perimeter point nearest the landing. Landing not inside an arena (debug spawn
        // elsewhere): a ring point FallbackDistance away.
        private static FPVector3 ResolveCrowdOrigin(Frame f, FPVector3 landing, CrowdMarkEffectData config)
        {
            FP height = landing.Y + config.ThrowHeight;

            if (LevelGenerationSystem.TryFindBossArenaChunk(f, out EntityRef chunkEntity) == true &&
                f.Unsafe.TryGetPointer<Chunk>(chunkEntity, out var chunk) == true &&
                f.Unsafe.TryGetPointer<Transform3D>(chunkEntity, out var chunkTransform) == true)
            {
                FP minX = chunkTransform->Position.X;
                FP minZ = chunkTransform->Position.Z;
                FP maxX = minX + chunk->ChunkSizeWidth;
                FP maxZ = minZ + chunk->ChunkSizeDepth;

                bool landingInArena = landing.X >= minX && landing.X <= maxX && landing.Z >= minZ && landing.Z <= maxZ;

                if (landingInArena == true)
                {
                    FP margin = config.OutsideMargin;
                    FPVector3 best = default;
                    FP bestSqr = FP.MaxValue;

                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        FPVector3 point = RandomPerimeterPoint(f, minX, minZ, maxX, maxZ, margin, height);
                        FP sqr = EnemyMovementUtility.FlatSqrDistance(point, landing);

                        if (sqr <= config.MaxThrowDistance * config.MaxThrowDistance)
                            return point;

                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            best = point;
                        }
                    }

                    FPVector3 nearest = NearestPerimeterPoint(landing, minX, minZ, maxX, maxZ, margin, height);
                    return EnemyMovementUtility.FlatSqrDistance(nearest, landing) < bestSqr ? nearest : best;
                }
            }

            FP angle = f.RNG->Next(FP._0, FP.PiTimes2);
            return new FPVector3(landing.X + FPMath.Cos(angle) * config.FallbackDistance, height, landing.Z + FPMath.Sin(angle) * config.FallbackDistance);
        }

        private static FPVector3 RandomPerimeterPoint(Frame f, FP minX, FP minZ, FP maxX, FP maxZ, FP margin, FP height)
        {
            int side = f.RNG->Next(0, 4);
            FP t = f.RNG->Next(FP._0, FP._1);

            switch (side)
            {
                case 0: return new FPVector3(minX - margin, height, FPMath.Lerp(minZ, maxZ, t));
                case 1: return new FPVector3(maxX + margin, height, FPMath.Lerp(minZ, maxZ, t));
                case 2: return new FPVector3(FPMath.Lerp(minX, maxX, t), height, minZ - margin);
                default: return new FPVector3(FPMath.Lerp(minX, maxX, t), height, maxZ + margin);
            }
        }

        private static FPVector3 NearestPerimeterPoint(FPVector3 p, FP minX, FP minZ, FP maxX, FP maxZ, FP margin, FP height)
        {
            FP toLeft = p.X - minX;
            FP toRight = maxX - p.X;
            FP toBottom = p.Z - minZ;
            FP toTop = maxZ - p.Z;
            FP min = FPMath.Min(FPMath.Min(toLeft, toRight), FPMath.Min(toBottom, toTop));

            if (min == toLeft) return new FPVector3(minX - margin, height, p.Z);
            if (min == toRight) return new FPVector3(maxX + margin, height, p.Z);
            if (min == toBottom) return new FPVector3(p.X, height, minZ - margin);
            return new FPVector3(p.X, height, maxZ + margin);
        }
    }
}
