namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // A leap onto the target that sends out a slowing wave on landing - the Frost Patriarch's Glacial Leap. Everything of
    // LeapDeliveryData (locked landing spot, ResolveValidLanding pulling it back onto real floor during the windup, the
    // kinematic arc, the landing hit on action.DamageRange with action.Effects, floor-gated by MaxHeightDifference), then:
    //
    //   stage 1 Air   - the jump (LeapDeliveryData.Tick). WarnLandingInAir: the landing spot's telegraph appears HERE, at
    //                   takeoff (an owner-bound ProjectileLandingWarning: the standard red ground warning, the landing
    //                   hit's DamageRange, for exactly the air time) instead of during the windup - the bear commits
    //                   first, then the floor shows where it comes down.
    //   stage 2 Wave  - from the touchdown point a ring expands at WaveSpeed up to WaveRadius; every player it sweeps over
    //                   (once - the band only moves outward), on the landing floor (WaveMaxHeightDifference), gets
    //                   WaveEffects (e.g. a non-Ice StickySlow). Outrunning the front avoids it.
    //
    // The stage flip lets the view play the landing pose/impact exactly at touchdown (IStagedDeliveryVisuals); the ring
    // outline is RingWaveVisualManager's, configured by the WaveVisual asset (a RingSlamDeliveryData used only for its
    // LineRendererPrefab). Not preemptable (like the leap it extends) - a stagger break waits for it to finish.
    public unsafe partial class LeapWaveDeliveryData : LeapDeliveryData
    {
        public const byte StageAir = 1;
        public const byte StageWave = 2;

        [ExpandableAsset] public List<AssetRef<HitEffectData>> WaveEffects = new();
        public FP WaveRadius = 7;
        public FP WaveSpeed = 9;
        public FP WaveMaxHeightDifference = FP.FromString("1.2");
        public AssetRef<RingSlamDeliveryData> WaveVisual;
        public FP WaveHeightOffset = FP._0_10;
        public bool WarnLandingInAir;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            bool done = base.Begin(f, ref filter, data, action, target);
            filter.Enemy->RingWaveRadius = FP._0;
            DeliveryStageUtility.Set(f, filter.Entity, StageAir);

            if (WarnLandingInAir == true)
            {
                FPVector3 landing = filter.Enemy->SkillTargetPosition;
                landing.Y = EnemyMovementUtility.ResolveGroundY(f, landing);
                f.Events.ProjectileLandingWarning(landing, JumpDuration, action.DamageRange, filter.Entity);
            }

            return done;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            bool inWave = f.TryGet<EnemyDeliveryStage>(filter.Entity, out EnemyDeliveryStage stage) == true && stage.Stage == StageWave;

            if (inWave == false)
            {
                if (base.Tick(f, ref filter, data, action, target) == false)
                    return false;

                // Touchdown (the landing hit just resolved): start the wave from here.
                FPVector3 center = filter.Transform3D->Position;
                filter.Enemy->SkillStartPosition = center;
                filter.Enemy->StateTimer = WaveSpeed > FP._0 ? WaveRadius / WaveSpeed : FP._0;
                filter.Enemy->RingWaveRadius = FP._0;
                DeliveryStageUtility.Set(f, filter.Entity, StageWave);

                if (WaveVisual.IsValid == true)
                    f.Events.RingWaveExpanding(filter.Entity, center, WaveHeightOffset, new AssetRef<EnemyDeliveryData>(WaveVisual.Id));

                return filter.Enemy->StateTimer <= FP._0 && Finish(f, filter.Entity);
            }

            FP total = WaveSpeed > FP._0 ? WaveRadius / WaveSpeed : FP._0;
            FP before = total - filter.Enemy->StateTimer;
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);
            FP after = total - filter.Enemy->StateTimer;

            FP previousFront = FPMath.Clamp(before * WaveSpeed, FP._0, WaveRadius);
            FP currentFront = FPMath.Clamp(after * WaveSpeed, FP._0, WaveRadius);
            filter.Enemy->RingWaveRadius = currentFront;

            if (currentFront > previousFront)
                SweepBand(f, ref filter, previousFront, currentFront);

            return (filter.Enemy->StateTimer <= FP._0 || currentFront >= WaveRadius) && Finish(f, filter.Entity);
        }

        // Players on the landing floor whose flat distance from the centre falls in [inner, outer) - the band the front
        // crossed this tick, so each is caught at most once.
        private void SweepBand(Frame f, ref EnemySystem.Filter filter, FP inner, FP outer)
        {
            FPVector3 center = filter.Enemy->SkillStartPosition;
            FP groundY = EnemyMovementUtility.ResolveGroundY(f, center);

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, new FPVector3(center.X, groundY, center.Z), groundY, outer, WaveMaxHeightDifference, players);

            for (int i = 0; i < count; i++)
            {
                EntityRef player = players[i];
                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == false)
                    continue;

                if (EnemyMovementUtility.FlatSqrDistance(center, transform->Position) < inner * inner)
                    continue;

                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = player,
                    Position = transform->Position,
                    PushDirection = transform->Position - center,
                    Damage = FP._0,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, WaveEffects, ref context, multiTarget: true);
            }
        }

        private static bool Finish(Frame f, EntityRef entity)
        {
            DeliveryStageUtility.Clear(f, entity);
            return true;
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            base.OnInterrupted(f, ref filter, data, action);
            DeliveryStageUtility.Clear(f, filter.Entity);
        }
    }
}
