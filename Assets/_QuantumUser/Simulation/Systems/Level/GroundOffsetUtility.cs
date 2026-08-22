namespace Quantum
{
    using Photon.Deterministic;

    // Small shared helpers around the GroundOffset component. The actual grounding is continuous and
    // lives in GroundSettleSystem (see GroundOffset.qtn) - this is only the "(re-)arm it" call plus
    // the collider-clearance math GroundSettleSystem and PopMotionSystem both need.
    public static unsafe class GroundOffsetUtility
    {
        // Re-arms an entity so GroundSettleSystem drops (or raises) it onto the ground again from
        // wherever it is now. A freshly created entity does NOT need this - it arrives with Enabled
        // authored true on its own prototype, which is the entire point of that flag. This exists for
        // an entity MOVED mid-life (RelocationProtocolSkillAction teleporting a Sentry to wherever Lux
        // was standing, which may well be mid-air), and is kept on the spawn paths as a cheap
        // guarantee that a spawn grounds itself even if someone forgets to tick Enabled on a new
        // prototype. FallVelocity resets so a fresh drop accelerates from rest instead of inheriting
        // the last one's speed. No-ops entirely for an entity with no GroundOffset at all.
        public static void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<GroundOffset>(entity, out var groundOffset) == false)
                return;

            groundOffset->Enabled = true;
            groundOffset->FallVelocity = FP._0;
        }

        // How far the entity's own collider bottom sits below its pivot - same "half-height minus
        // Centroid.Y" math as ProjectileSystem.ResolveRestOffset, reusing EnemyMovementUtility's
        // shared per-shape switch instead of a third copy. Without this, GroundOffset.Offset placed
        // the pivot itself at groundY + Offset, which only reads as "resting on the ground" for a
        // shape with zero height below its own pivot - anything else (a sphere/box/capsule centered
        // on the pivot, as colliders here always are) sank in by its own half-height. 0 for an entity
        // with no collider at all, same as ResolveEntityRadius's fallback. Shared by GroundSettleSystem
        // (every tick while an entity is still settling) and PopMotionSystem (every tick while an orb
        // is mid-arc), so both resolve the identical resting height.
        public static FP ResolveGroundClearance(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return FP._0;

            return EnemyMovementUtility.ResolveShapeHalfHeight(collider->Shape) - collider->Shape.Centroid.Y;
        }
    }
}
