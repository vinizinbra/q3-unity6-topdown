namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drops GroundTrail.Prototype every SpawnDistance the carrier travels - see GroundTrail.qtn.
    [Preserve]
    public unsafe class GroundTrailSystem : SystemMainThreadFilter<GroundTrailSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            GroundTrail* trail = filter.GroundTrail;
            FPVector3 position = filter.Transform3D->Position;

            if (trail->HasLastDrop == false)
            {
                trail->LastDropPosition = position;
                trail->HasLastDrop = true;
                return;
            }

            if (f.Has<Burrowed>(filter.Entity) == true)
            {
                // Resume from wherever it resurfaces rather than dropping one piece at the exit point
                // for the whole underground stretch.
                trail->LastDropPosition = position;
                return;
            }

            if (f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == true && health->CurrentHealth <= FP._0)
                return;

            FPVector3 delta = position - trail->LastDropPosition;
            delta.Y = FP._0;

            if (delta.SqrMagnitude < trail->SpawnDistance * trail->SpawnDistance)
                return;

            trail->LastDropPosition = position;
            SpawnedEntitySpawner.Spawn(f, filter.Entity, trail->Prototype, trail->Duration, position, scale: trail->Scale);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public GroundTrail* GroundTrail;
        }
    }
}
