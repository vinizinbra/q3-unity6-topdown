namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // MortarBarrageDeliveryData's aimed/scattered ground-point idiom with the projectile removed
    // entirely - no shell ever flies, so there's nothing to solve a launch/arc for and nothing for
    // ProjectileSystem to simulate. Unlike Mortar (which resolves every shell's point up front, since
    // they're all launched in the same Begin()), points here spawn ONE AT A TIME: telegraph, wait
    // Delay, detonate, wait Stagger, telegraph the next - each spawn re-resolves the target's LIVE
    // position rather than reusing a stale one, so a staggered run of points actually chases a moving
    // target instead of leaving later points aimed at wherever it stood several seconds earlier
    // (author MinRandomOffset/MaxRandomOffset non-zero for the scattered ones to actually separate
    // from that live anchor - same caveat ScatterDeliveryData's own comment gives).
    //
    // Stagger = 0 (the default) collapses this into one simultaneous burst - Tick's own while loop
    // keeps spawning+detonating every remaining point in the same tick with no gap, so every asset
    // authored before Stagger existed keeps its exact old behavior (all points aimed at the one
    // position resolved at Begin()). Stagger > 0 spaces them out in real time instead.
    //
    // Detonation goes through Hit's own AreaHitData.Detonate - the exact same "reused purely as data"
    // pattern MortarBarrageDeliveryData's shells already use (Hit.Effects/TargetMask/
    // MaxHeightDifference decide who/what gets hurt, Hit.BlastRadius is the single source of truth for
    // both the telegraph size and the real blast so they can never drift apart, and
    // Hit.BlastEffectPrefab gets its existing AreaDetonated/EffectsManager.OnAreaDetonated hookup for
    // free - no new event needed for the radius-scaled impact VFX). Author TargetMask = Players on
    // this Hit asset (Both, the default, would also catch this enemy itself and any other enemy
    // standing in the blast) and MaxHeightDifference > 0 if this should be a flat ground-area hit
    // rather than a volumetric sphere.
    //
    // Meant as a SequenceDeliveryData step following a Leap (or any other instant delivery) - e.g.
    // the enemy leaps, lands, and this step scatters a few delayed ground bursts around the landing
    // spot a beat later, punishing whoever stands still. Always multi-tick (Begin() returns false
    // whenever it resolved at least one point) - see LeapDeliveryData's own comment for why
    // Void Pressure only ever scales an Active-phase Tick like this one, never the windup.
    public unsafe class GroundBarrageDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<AreaHitData> Hit;

        public int PointCount = 3;

        // How many of PointCount land exactly on the (live-resolved, at their OWN spawn time) anchor
        // rather than through RandomizeAroundAnchor. Clamped to [0, PointCount].
        public int AimedPointCount = 1;

        // How long each point telegraphs before it detonates.
        public FP Delay = 1;

        // Gap between one point's detonation and the next point's spawn - 0 (the default) chains
        // straight through every point in the same Tick with no gap (see class comment).
        public FP Stagger = 0;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            int count = Math.Clamp(PointCount, 0, byte.MaxValue);

            if (count == 0)
                return true; // misauthored asset - nothing to wait on

            filter.Enemy->PendingImpactTotal = (byte)count;
            filter.Enemy->PendingImpactIndex = 0;
            filter.Enemy->PendingImpactAwaitingSpawn = false;

            SpawnPoint(f, ref filter, action, target, pointIndex: 0);
            filter.Enemy->StateTimer = Delay;
            return false;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Void Pressure (Kai) - same reasoning as LeapDeliveryData/RingSlamDeliveryData's
            // identical comment: only this Active-phase Tick is ever scaled, never the windup.
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            AreaHitData hitData = f.FindAsset(Hit);

            // Carries over any leftover (possibly negative) StateTimer into the next phase's own
            // countdown instead of resetting to a fixed value, so frame-rate/Void-Pressure variance
            // can't drift the schedule - same idiom the old drain loop used, just alternating between
            // two phases (spawn a point / detonate the spawned point) instead of one.
            while (filter.Enemy->StateTimer <= FP._0)
            {
                if (filter.Enemy->PendingImpactAwaitingSpawn == true)
                {
                    filter.Enemy->PendingImpactAwaitingSpawn = false;
                    SpawnPoint(f, ref filter, action, target, filter.Enemy->PendingImpactIndex);
                    filter.Enemy->StateTimer += Delay;
                    continue;
                }

                // hitIndex = PendingImpactIndex (not the default 0) - two points landing close enough
                // to overlap and both resolving in the same tick (Stagger <= 0) would otherwise
                // produce byte-identical EntityDamaged events for a target caught in both (same
                // Target/Damage/Position within one tick), which Quantum silently collapses into a
                // single hit. Strictly increasing across this whole run, so every call is distinct.
                // See AreaHitData.Detonate's own comment.
                hitData.Detonate(f, filter.Entity, DamageSource.None, ElementType.Neutral, action.Damage,
                    spawnDepth: 0, filter.Enemy->PendingImpactPoint, hitIndex: filter.Enemy->PendingImpactIndex);

                filter.Enemy->PendingImpactIndex++;

                if (filter.Enemy->PendingImpactIndex >= filter.Enemy->PendingImpactTotal)
                    return true;

                filter.Enemy->PendingImpactAwaitingSpawn = true;
                filter.Enemy->StateTimer += Stagger;
            }

            return false;
        }

        // Resolves and telegraphs pointIndex's own point - called once from Begin() for point 0, then
        // once per point after that from Tick() as each one's turn comes up. Re-resolves the anchor
        // fresh every time (see ResolveLiveAnchor) rather than reusing Enemy.SkillTargetPosition, so a
        // moving target is actually chased across the whole staggered run instead of every point aiming
        // at wherever it stood back when windup first locked that field.
        private void SpawnPoint(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, EntityRef target, int pointIndex)
        {
            FPVector3 anchor = ResolveLiveAnchor(f, ref filter, action, target);
            int aimedCount = Math.Clamp(AimedPointCount, 0, filter.Enemy->PendingImpactTotal);
            FPVector3 point = pointIndex < aimedCount ? anchor : RandomizeAroundAnchor(f, anchor);

            // The anchor's Y is whatever action.IgnoreY resolved it to (typically the ENEMY's own Y,
            // not the real floor under this specific point - see ResolveIgnoreY) - fine for the
            // telegraph circle (GroundWarningTelegraphManager separately raycasts to snap it onto the
            // visual ground), but AreaDetonated's own burst VFX (EffectsManager.OnAreaDetonated) has
            // no such correction and would spawn floating/sunk wherever this Y actually lands. Snapping
            // here instead makes it the one authoritative ground height shared by the telegraph, the
            // real hit, and the VFX - same idiom LeapDeliveryData's own landing-spot snap already uses.
            if (EnemyMovementUtility.TryFindGroundHeight(f, point, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY) == true)
                point.Y = groundY;

            filter.Enemy->PendingImpactPoint = point;
            f.Events.ProjectileLandingWarning(point, Delay, f.FindAsset(Hit).BlastRadius);
        }

        // action.Origin == Self already reads live every call (the enemy's own current position, not
        // a locked snapshot) - only the TargetAnchor branch needs an explicit live re-fetch, since
        // Enemy.SkillTargetPosition is a one-time capture (by OnAnticipating, or overwritten by a
        // preceding SequenceDeliveryData step like LeapDeliveryData's own landing spot) rather than
        // something that tracks the target afterward. Falls back to that locked SkillTargetPosition
        // only if the target is already gone (dead/despawned) by this point's own spawn time - there's
        // nothing live left to chase.
        private static FPVector3 ResolveLiveAnchor(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, EntityRef target)
        {
            if (action.Origin == EnemyActionOrigin.Self)
                return filter.Transform3D->Position;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
                return EnemyMovementUtility.ResolveIgnoreY(filter.Transform3D->Position, targetPosition, action.IgnoreY);

            return filter.Enemy->SkillTargetPosition;
        }
    }
}
