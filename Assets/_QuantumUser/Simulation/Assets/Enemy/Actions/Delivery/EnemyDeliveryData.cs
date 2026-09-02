namespace Quantum
{
    using Photon.Deterministic;

    // Base for every enemy action's execution logic - each subclass is its own Quantum asset
    // owning Begin/Tick, the same "one asset per behavior" shape AttackData used to be directly.
    // Referenced via AssetRef<EnemyDeliveryData> from an EnemyActionData, so the same delivery can
    // be reused across many actions/enemies with different tuning. Shared/reused - Begin/Tick/
    // OnAnticipating must stay pure functions of (Frame, ref Filter, ...), no mutable fields
    // written at runtime.
    public abstract unsafe partial class EnemyDeliveryData : AssetObject
    {
        // Both 0 (default): no randomization, RandomizeAroundAnchor just returns anchor unchanged -
        // every existing delivery keeps its exact current behavior unless it opts in. A ring, not a
        // filled disc (see EnemyMovementUtility.RandomPositionInRing) - MinOffset > 0 keeps the
        // result off the anchor itself.
        public FP MinRandomOffset;
        public FP MaxRandomOffset;

        // Shared by any concrete delivery that wants its resolved anchor scattered instead of
        // landing exactly on it (e.g. ScatterDeliveryData's scattered points) - not called
        // automatically, since most deliveries (melee, charge) want their hit-check anchor exact.
        protected FPVector3 RandomizeAroundAnchor(Frame f, FPVector3 anchor)
        {
            if (MaxRandomOffset <= FP._0)
                return anchor;

            return EnemyMovementUtility.RandomPositionInRing(f, anchor, MinRandomOffset, MaxRandomOffset);
        }

        // Fires ProjectileLandingWarning for a lobbed shot, deriving its real flight time from the
        // solved launch's own horizontal speed rather than a separately-authored guess - shared by
        // any delivery that lobs onto a known ground point (MortarBarrageDeliveryData per shell,
        // ProjectileDeliveryData's own UseArc/ShowLandingWarning branch), so the ground-warning
        // telegraph (see GroundWarningTelegraphManager, View) is one shared mechanism instead of
        // each delivery re-deriving flight time and firing its own event. No-ops (fires nothing) if
        // origin and point coincide, velocity has no horizontal component, or radius is 0 - there's
        // nothing meaningful to warn about.
        protected static void FireLandingWarning(Frame f, FPVector3 origin, FPVector3 point, FPVector3 velocity, FP radius)
        {
            if (radius <= FP._0)
                return;

            FPVector3 flatDelta = new FPVector3(point.X - origin.X, FP._0, point.Z - origin.Z);
            FPVector3 flatVelocity = new FPVector3(velocity.X, FP._0, velocity.Z);
            FP flightTime = flatVelocity.Magnitude > FP._0 ? flatDelta.Magnitude / flatVelocity.Magnitude : FP._0;

            if (flightTime > FP._0)
                f.Events.ProjectileLandingWarning(point, flightTime, radius);
        }

        // A projectile's Hit only sometimes carries a meaningful blast radius (AreaHitData) - a
        // direct-hit/pierce type has none. Reading it back here rather than authoring a second
        // WarningRadius field on the delivery is the same "single source of truth" fix as routing
        // the arc launch itself through the assigned ProjectileMovementData instead of duplicating
        // LaunchAngle/Gravity (see MortarBarrageDeliveryData's own history) - the ground-warning
        // circle can never silently drift out of sync with the real blast it's warning about.
        // Returns 0 (FireLandingWarning's own no-op case) for anything else.
        protected static FP ResolveWarningRadius(Frame f, AssetRef<ProjectileHitData> hit)
        {
            return hit.IsValid == true && f.FindAsset(hit) is AreaHitData areaHit ? areaHit.BlastRadius : FP._0;
        }

        // Called before the windup timer is checked each Preparation/Telegraph tick. elapsed is the
        // fraction (0-1) of action.AnticipationTime that has passed so far this tick (pre-decrement -
        // see EnemySystem.UpdatePreparation), only meaningful when AimLock == LocksAtPercent. Override
        // for non-track/lock windup behavior.
        public virtual void OnAnticipating(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target, FP elapsed)
        {
            if (action.DirectionTracking == DirectionUpdateMode.DoNotUpdateTargetDirection)
                return; // never re-aims - locked at whatever was captured when Preparation began

            if (action.AimLock == EnemyAimLockTiming.LocksAtAnticipationStart)
                return; // same effect as DoNotUpdateTargetDirection above, expressed via AimLock instead

            // Phase hasn't flipped to Telegraph yet on the exact tick it crosses TelegraphStartPercent
            // (EnemySystem.UpdatePreparation calls this before that check) - one extra tick of
            // tracking before the freeze below actually takes hold is imperceptible, not worth
            // reordering the caller for.
            if (action.AimLock == EnemyAimLockTiming.LocksAtTelegraphStart && filter.Enemy->Phase == EnemyActionPhase.Telegraph)
                return; // froze the instant windup crossed into Telegraph - stop re-aiming from here on

            if (action.AimLock == EnemyAimLockTiming.LocksAtPercent && elapsed >= action.AimLockPercent)
                return; // froze once the windup crossed the authored AimLockPercent - independent of Telegraph

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return;

            EnemyMovementUtility.FaceTarget(filter.Aim, filter.Transform3D->Position, targetPosition);
            filter.Enemy->SkillTargetPosition = EnemyMovementUtility.ResolveIgnoreY(filter.Transform3D->Position, targetPosition, action.IgnoreY);
        }

        // Gate checked BEFORE the enemy commits to Preparation/Telegraph (EnemySystem.UpdateChasing,
        // right after EnemyDecisionUtility.TrySelectAction picks this action) - default true (every
        // existing delivery keeps its exact current behavior). Override when a delivery can be
        // picked by distance/cooldown alone yet still be un-executable from here right now (e.g.
        // ChargeDeliveryData's straight-line dash path being wall/ledge-blocked) - returning false
        // skips Preparation entirely for this tick so the enemy falls through to its normal chase
        // movement instead, re-evaluating next tick as it (or the target) repositions. Once this
        // returns true and the telegraph actually plays, Begin() below must commit unconditionally -
        // see ChargeDeliveryData.Begin's own comment for why.
        public virtual bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            return true;
        }

        // Return true if the action resolves this same tick (melee/projectile); false if it needs
        // Tick() first (e.g. a dash).
        public abstract bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target);

        // Called only if Begin() returned false. Return true once finished.
        public virtual bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            return true;
        }

        // Called once if an in-progress Active delivery gets interrupted (see
        // EnemyActionData.InterruptibleDuringActive), right before EnemySystem hands the enemy off
        // to Recovery - default no-op. Override to clean up mid-execution state instead of just
        // being cut off (e.g. a future non-kinematic delivery decelerating smoothly rather than
        // stopping dead). Not called for a Preparation/Telegraph interrupt - see
        // EnemySystem.CancelWindup, which never lets Begin() run at all.
        public virtual void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
        }
    }
}
