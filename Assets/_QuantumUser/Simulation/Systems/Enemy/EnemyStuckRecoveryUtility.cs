namespace Quantum
{
    using Photon.Deterministic;

    // Safety net for an enemy that a knockback drove INTO level geometry instead of against it.
    //
    // Enemies are ordinary dynamic PhysicsBody3D's and the Enemy layer does collide with Ground, so
    // the overwhelmingly common case is exactly what it should be: a shoved enemy hits a wall and
    // stops. But a hard enough push (Brute's Iron Shoulder sets velocity to 20 u/s, his Groundbreaker
    // up to 16.5, both KnockbackApplyMode.Override) can move a body far enough in a single physics
    // step - especially into a corner of a chunk's COMPOUND collider, or into a seam between two
    // adjacent chunks, which this project already has documented gaps at - that the solver doesn't
    // recover it. Quantum's 3D physics has no continuous collision detection to fall back on.
    //
    // Once the enemy's center is inside the geometry it never gets out on its own, because every wall
    // check EnemyMovementUtility steers by (IsBlockedByWall and friends) raycasts FROM the enemy's own
    // position - from in there, there is no wall ahead to avoid, so EnemySystem happily drives it
    // deeper on the next tick. That is the reported symptom: the enemy walks into the environment and
    // sticks.
    //
    // Deliberately a RECOVERY rather than a clamp on the knockback itself. Capping every impulse
    // against a wall probe would flatten how knockback feels anywhere near a wall (which is most of an
    // arena), would have to guess at drag to know how far a push actually carries, and still wouldn't
    // catch an enemy popped up and OVER a wall - Juggernaut's Discharge imparts +16 u/s upward, which
    // against the project's -40 gravity apexes around 3.2 units. This catches every one of those the
    // same way, and changes no combat numbers at all.
    //
    // Cost is only paid by enemies somebody actually knocked around: OnEnemyKnockedBack opens the
    // window and records the (known-good) spot the enemy was standing in at that moment, and only
    // while that window is open does anything here run.
    public static unsafe class EnemyStuckRecoveryUtility
    {
        // How long after a knockback to keep watching. Generous next to any real knockback settle time
        // (EnemyTierStatsConfig.KnockbackRecoveryTime is a fraction of this) because it also has to
        // cover an arc that goes over a wall and only lands - and only becomes a problem - well after
        // the stagger itself is over.
        //
        // Not authored on a config asset on purpose: this is a physics safety net, not a balance knob.
        // There is no gameplay reason to ever tune it, and exposing it would invite someone to.
        private static readonly FP WatchDuration = 3;

        // Fraction of the enemy's own collider radius used as the probe sphere. Well under 1 so
        // resting flush against a wall (center exactly one radius from the surface) can never trip
        // this - only a center that has genuinely sunk into the geometry does. Also keeps the probe
        // clear of the floor underneath, which is on the same Ground layer.
        private static readonly FP ProbeRadiusFraction = FP._0_50;

        // Same HitStatics | HitKinematics pair every other wall probe in this codebase uses - a
        // chunk's compound collider is a kinematic entity collider (ChunkCompoundColliderBuilder bakes
        // it onto the chunk's own QuantumEntityPrototype), so HitStatics alone would miss every wall
        // in the level.
        private const QueryOptions GeometryQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

        // Called by DamageUtility's knockback signal handler, while the enemy is still standing
        // somewhere valid - that position is what it gets returned to if the push buries it.
        public static void OnKnockedBack(Frame f, EntityRef entity, Enemy* enemy)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            enemy->PreKnockbackPosition = transform->Position;
            enemy->StuckCheckTimer = WatchDuration;
        }

        // Called every tick from EnemySystem.Update. Returns true if it actually had to rescue the
        // enemy this tick, purely so the caller can skip the rest of its own movement work rather
        // than immediately driving it somewhere else again.
        public static bool Tick(Frame f, EntityRef entity, Enemy* enemy, Transform3D* transform, PhysicsBody3D* body)
        {
            if (enemy->StuckCheckTimer <= FP._0)
                return false;

            enemy->StuckCheckTimer -= f.DeltaTime;

            if (IsInsideGeometry(f, entity) == false)
                return false;

            transform->Position = enemy->PreKnockbackPosition;

            // Whatever velocity carried it in there would carry it straight back in on the next step.
            if (body != null && body->IsKinematic == false)
            {
                body->Velocity = FPVector3.Zero;
            }

            // One rescue per knockback is enough - leaving the window open would re-run the probe
            // every tick for an enemy that is already back where it belongs.
            enemy->StuckCheckTimer = FP._0;

            Log.Debug($"[Enemy] {entity} was pushed inside level geometry - restored to {enemy->PreKnockbackPosition}");

            return true;
        }

        // A sphere well inside the enemy's own footprint, at its true collider center (Transform3D
        // position plus the shape's own Centroid) rather than its origin.
        private static bool IsInsideGeometry(Frame f, EntityRef entity)
        {
            FP radius = EnemyMovementUtility.ResolveEntityRadius(f, entity);

            if (radius <= FP._0)
                return false;

            FPVector3 center = EnemyMovementUtility.ResolveEntityCenter(f, entity);
            Shape3D probe = Shape3D.CreateSphere(radius * ProbeRadiusFraction);

            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, probe,
                EnemyMovementUtility.GetGroundLayerMask(f), GeometryQueryOptions);

            return hits.Count > 0;
        }
    }
}
