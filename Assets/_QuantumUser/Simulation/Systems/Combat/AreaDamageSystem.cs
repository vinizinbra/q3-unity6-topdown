namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Re-applies a spawned area's effects on its interval; DestroyAfterTime ends it. The
    // PhysicsCollider3D in the filter is the area - a prototype without one does nothing, which is
    // why it's a required filter component rather than an optional lookup.
    [Preserve]
    public unsafe class AreaDamageSystem : SystemMainThreadFilter<AreaDamageSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.AreaDamage->TickTimer -= f.DeltaTime;

            if (filter.AreaDamage->TickTimer > FP._0)
                return;

            filter.AreaDamage->TickTimer = filter.AreaDamage->TickInterval;

            f.Events.AreaDamageTicked(filter.Entity);

            AreaOwnerUtility.Resolve(f, filter.Entity, out EntityRef owner, out DamageSource source, out ElementType element);

            HitEffectUtility.ApplyInCollider(f, filter.AreaDamage->Effects, filter.Transform3D,
                filter.PhysicsCollider3D, owner, filter.AreaDamage->Damage, source, ResolvePushDirection(ref filter), element,
                filter.AreaDamage->TargetMask, filter.Entity);
        }

        // Local space: rotated by the entity's own facing so a blast spawned with
        // SpawnAlignment.Facing pushes wherever the caster aimed it - see AreaDamage.qtn.
        private static FPVector3? ResolvePushDirection(ref Filter filter)
        {
            if (filter.AreaDamage->OverridePushDirection == false)
                return null;

            return filter.Transform3D->Rotation * filter.AreaDamage->PushDirection;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public PhysicsCollider3D* PhysicsCollider3D;
            public AreaDamage* AreaDamage;
        }
    }
}
