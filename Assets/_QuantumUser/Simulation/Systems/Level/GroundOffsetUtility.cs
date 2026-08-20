namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared by SpawnedEntitySpawner (a skill/projectile-impact spawn, ground-checked right after
    // Transform3D.Position is set), MapGroundSettleSystem (a map-baked entity, ground-checked once it
    // materializes at its own hand-placed position) and RelocationProtocolSkillAction (an entity MOVED
    // mid-life to wherever the caster was standing, which may be mid-air) - all three need the exact
    // same raycast/target-Y/settle-or-snap resolution against a GroundOffset, just from different
    // trigger points.
    public static unsafe class GroundOffsetUtility
    {
        // Raycasts straight down from the entity's current XZ. Snaps immediately unless the relevant
        // direction's approach rate is authored (descending reads FallGravityMultiplier, ascending
        // reads FloatSpeed) - only then is this worth spreading across ticks via
        // SettlingToGround/GroundSettleSystem instead of just placing the entity there once, up front.
        public static void Apply(Frame f, EntityRef entity, Transform3D* transform)
        {
            if (f.Unsafe.TryGetPointer<GroundOffset>(entity, out var groundOffset) == false)
                return;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, transform->Position, groundLayerMask, out FP groundY) == false)
            {
                Log.Error($"[GroundOffset] {entity} has a GroundOffset but no ground was found beneath {transform->Position} - left at spawn Y");
                return;
            }

            FP targetY = groundY + ResolveGroundClearance(f, entity) + groundOffset->Offset;
            FP approachRate = targetY < transform->Position.Y ? groundOffset->FallGravityMultiplier : groundOffset->FloatSpeed;

            if (approachRate <= FP._0)
            {
                transform->Position = new FPVector3(transform->Position.X, targetY, transform->Position.Z);
                return;
            }

            // AddOrGet, and both fields written explicitly: this can legitimately run more than once
            // on the same entity (a relocated sentry re-grounding at a new spot), and a plain Add
            // would silently keep a stale TargetY from the previous settle. FallVelocity resets so a
            // fresh drop accelerates from rest instead of inheriting the last one's speed.
            f.AddOrGet<SettlingToGround>(entity, out var settling);
            settling->TargetY = targetY;
            settling->FallVelocity = FP._0;
        }

        // How far the entity's own collider bottom sits below its pivot - same "half-height minus
        // Centroid.Y" math as ProjectileSystem.ResolveRestOffset, reusing EnemyMovementUtility's
        // shared per-shape switch instead of a third copy. Without this, GroundOffset.Offset placed
        // the pivot itself at groundY + Offset, which only reads as "resting on the ground" for a
        // shape with zero height below its own pivot - anything else (a sphere/box/capsule centered
        // on the pivot, as colliders here always are) sank in by its own half-height. 0 for an entity
        // with no collider at all, same as ResolveEntityRadius's fallback. Public so PopMotionSystem
        // can resolve the same resting-height clearance every tick while an orb is mid-arc, not just
        // once here at spawn.
        public static FP ResolveGroundClearance(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return FP._0;

            return EnemyMovementUtility.ResolveShapeHalfHeight(collider->Shape) - collider->Shape.Centroid.Y;
        }
    }
}
