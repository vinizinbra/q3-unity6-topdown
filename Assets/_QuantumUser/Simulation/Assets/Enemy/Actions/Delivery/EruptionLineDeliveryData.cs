namespace Quantum
{
    using Photon.Deterministic;

    // Slam + lines of delayed eruptions - the Frost Patriarch's Icebreaker. Unlike GroundBarrageDeliveryData (which
    // re-resolves the LIVE target for every point, so a staggered run chases), every eruption point here is COMMITTED in
    // Begin along the direction locked at the end of the windup, and all of them are telegraphed at once, each with its
    // own countdown - nothing follows the player afterwards.
    //
    //   Begin  - SlamHit detonates SlamDistance in front of the boss (the double-paw impact, a short area), then
    //            LineCount lines fan out LineSpreadDegrees apart around the locked direction (1 = a single straight line).
    //            Each line's points sit at FirstDistance + i * Spacing; a line stops at its first point that is behind a
    //            wall/ledge, over a void, or on a floor further than MaxHeightDifference from the boss's own - it never
    //            erupts on another level or through geometry. Every point gets an owner-bound ProjectileLandingWarning
    //            (fades by itself if the boss is staggered), sized to its own radius - all of them here, unless
    //            WarningLead staggers them (below).
    //   Tick   - wave i (point i of every line) detonates EruptionHit at FirstDelay + i * Interval, each wave
    //            RadiusGrowthPerStep bigger than the last (the lines "grow" as they advance). With WarningLead > 0 a
    //            wave's warnings only appear WarningLead before it detonates (= Interval: one wave telegraphed at a
    //            time, the next one showing as the previous erupts - a readable floor instead of every marker at once).
    //
    // State: Enemy.SkillStartPosition (origin on the floor), SkillTargetPosition (origin + locked direction),
    // PendingImpactIndex/Total (next wave / waves with at least one valid point), StateTimer (elapsed). Geometry is static
    // during the action, so a point's validity is recomputed identically at detonation. Interruptible by a stagger break
    // (AllowsForcedPreemption): pending eruptions simply never detonate - nothing fires on cancel.
    public unsafe partial class EruptionLineDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<AreaHitData> SlamHit;
        public FP SlamDistance = FP._1_50;

        [ExpandableAsset] public AssetRef<AreaHitData> EruptionHit;
        public int EruptionCount = 3;
        public FP FirstDistance = 3;
        public FP Spacing = FP.FromString("2.2");
        public FP FirstDelay = FP.FromString("0.6");
        public FP Interval = FP.FromString("0.35");
        public FP MaxHeightDifference = FP.FromString("1.2");
        public FP SlamDamage = 20;

        public int LineCount = 1;
        public FP LineSpreadDegrees = 30;
        // Radius multiplier added per wave (0.2 = wave 0 x1, wave 1 x1.2, wave 2 x1.4).
        public FP RadiusGrowthPerStep = FP._0;
        // >0: each wave after the first is warned only this long before it detonates (the first: in Begin, the whole
        // FirstDelay). 0 = every wave warned at once in Begin.
        public FP WarningLead = FP._0;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 position = filter.Transform3D->Position;
            FPVector3 direction = filter.Enemy->SkillTargetPosition - position;
            direction.Y = FP._0;
            direction = direction.SqrMagnitude > FP._0 ? direction.Normalized : FPQuaternion.Euler(0, filter.Aim->Angle, 0) * FPVector3.Forward;

            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FP groundY = EnemyMovementUtility.ResolveGroundY(f, position);
            FPVector3 origin = new FPVector3(position.X, groundY, position.Z);

            filter.Enemy->SkillStartPosition = origin;
            filter.Enemy->SkillTargetPosition = origin + direction;
            filter.Aim->Angle = FPMath.Atan2(direction.X, direction.Z) * FP.Rad2Deg;

            // The double-paw impact itself.
            if (SlamHit.IsValid == true && TryGetPoint(f, origin, direction, SlamDistance, groundY, groundMask, out FPVector3 slamPoint) == true)
                f.FindAsset(SlamHit).Detonate(f, filter.Entity, DamageSource.None, ElementType.Neutral, SlamDamage, spawnDepth: 0, slamPoint, hitIndex: 0);

            // Commit and telegraph every line now.
            AreaHitData eruption = EruptionHit.IsValid == true ? f.FindAsset(EruptionHit) : null;
            int waves = 0;

            if (eruption != null)
            {
                for (int line = 0; line < LineCount; line++)
                {
                    FPVector3 lineDirection = LineDirection(direction, line);

                    for (int i = 0; i < EruptionCount && i < 64; i++)
                    {
                        if (TryGetPoint(f, origin, lineDirection, FirstDistance + Spacing * i, groundY, groundMask, out FPVector3 point) == false)
                            break;

                        if (WarningTime(i) <= FP._0)
                            f.Events.ProjectileLandingWarning(point, FirstDelay + Interval * i, eruption.BlastRadius * RadiusAt(i), filter.Entity);
                        waves = i + 1 > waves ? i + 1 : waves;
                    }
                }
            }

            filter.Enemy->PendingImpactTotal = (byte)waves;
            filter.Enemy->PendingImpactIndex = 0;
            filter.Enemy->StateTimer = FP._0;
            return waves == 0;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FP previous = filter.Enemy->StateTimer;
            filter.Enemy->StateTimer += f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            FPVector3 origin = filter.Enemy->SkillStartPosition;
            FPVector3 direction = filter.Enemy->SkillTargetPosition - origin;
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            AreaHitData eruption = f.FindAsset(EruptionHit);

            // Staggered telegraph: the waves whose warning time was crossed this tick.
            if (WarningLead > FP._0)
            {
                for (int wave = filter.Enemy->PendingImpactIndex; wave < filter.Enemy->PendingImpactTotal; wave++)
                {
                    FP warnAt = WarningTime(wave);
                    if (warnAt <= previous || warnAt > filter.Enemy->StateTimer)
                        continue;

                    for (int line = 0; line < LineCount; line++)
                    {
                        if (TryGetLinePoint(f, origin, LineDirection(direction, line), wave, groundMask, out FPVector3 point) == true)
                            f.Events.ProjectileLandingWarning(point, FirstDelay + Interval * wave - filter.Enemy->StateTimer,
                                eruption.BlastRadius * RadiusAt(wave), filter.Entity);
                    }
                }
            }

            while (filter.Enemy->PendingImpactIndex < filter.Enemy->PendingImpactTotal &&
                   filter.Enemy->StateTimer >= FirstDelay + Interval * filter.Enemy->PendingImpactIndex)
            {
                int wave = filter.Enemy->PendingImpactIndex;

                for (int line = 0; line < LineCount; line++)
                {
                    if (TryGetLinePoint(f, origin, LineDirection(direction, line), wave, groundMask, out FPVector3 point) == false)
                        continue;

                    eruption.Detonate(f, filter.Entity, DamageSource.None, ElementType.Neutral, action.Damage,
                        spawnDepth: 0, point, RadiusAt(wave), hitIndex: (byte)(1 + wave * LineCount + line));
                }

                filter.Enemy->PendingImpactIndex++;
            }

            return filter.Enemy->PendingImpactIndex >= filter.Enemy->PendingImpactTotal;
        }

        private FPVector3 LineDirection(FPVector3 direction, int line)
        {
            if (LineCount <= 1)
                return direction;

            FP offset = (line - (LineCount - 1) * FP._0_50) * LineSpreadDegrees;
            return FPQuaternion.Euler(0, offset, 0) * direction;
        }

        private FP RadiusAt(int wave) => FP._1 + RadiusGrowthPerStep * wave;

        // When wave `wave`'s warnings appear (elapsed time; <= 0 = in Begin). The first wave is always warned in Begin
        // (the whole FirstDelay); the rest WarningLead before their own detonation.
        private FP WarningTime(int wave) =>
            WarningLead > FP._0 && wave > 0 ? FPMath.Max(FP._0, FirstDelay + Interval * wave - WarningLead) : FP._0;

        // Point `wave` of a line, valid only if every point before it on that line is too (the line stopped there).
        private bool TryGetLinePoint(Frame f, FPVector3 origin, FPVector3 lineDirection, int wave, int groundMask, out FPVector3 point)
        {
            point = default;

            for (int i = 0; i <= wave; i++)
            {
                if (TryGetPoint(f, origin, lineDirection, FirstDistance + Spacing * i, origin.Y, groundMask, out point) == false)
                    return false;
            }

            return true;
        }

        // A valid point on the boss's own floor: reachable from the origin without crossing a wall/ledge (ray at knee
        // height along the floor), solid ground under it, within MaxHeightDifference of the origin floor.
        private bool TryGetPoint(Frame f, FPVector3 origin, FPVector3 direction, FP distance, FP originGroundY, int groundMask, out FPVector3 point)
        {
            point = origin + direction * distance;

            if (EnemyMovementUtility.IsBlockedByWall(f, new FPVector3(origin.X, originGroundY + FP._0_50, origin.Z), direction, distance, groundMask) == true)
                return false;

            if (EnemyMovementUtility.TryFindGroundHeight(f, point, groundMask, out FP groundY) == false ||
                FPMath.Abs(groundY - originGroundY) > MaxHeightDifference)
                return false;

            point.Y = groundY;
            return true;
        }

        public override bool AllowsForcedPreemption(Frame f, EntityRef entity) => true;
    }
}
