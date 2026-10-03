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

            bool burrowed = f.Has<Burrowed>(filter.Entity);
            bool active = trail->Mode switch { 1 => burrowed, 2 => true, _ => burrowed == false };

            if (active == true && trail->OnlyDuringAction.IsValid == true)
                active = IsExecutingAction(f, filter.Entity, trail->OnlyDuringAction);

            if (active == false)
            {
                // Resume from wherever it becomes active again rather than dropping one piece at that
                // point for the whole inactive stretch.
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

        private static bool IsExecutingAction(Frame f, EntityRef entity, AssetRef<EnemyActionData> action)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false || enemy->Phase != EnemyActionPhase.Active)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return data != null && EnemyDecisionUtility.ResolveActionRef(data, enemy->CurrentActionSlot) == action;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public GroundTrail* GroundTrail;
        }
    }
}
