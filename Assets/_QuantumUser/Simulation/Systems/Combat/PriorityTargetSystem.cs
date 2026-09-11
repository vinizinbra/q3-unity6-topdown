namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every owner's PriorityTarget down and clears it - either once Remaining expires, or
    // immediately the tick the target itself becomes invalid (destroyed, or an Enemy that died),
    // whichever comes first - so "Focus clears immediately if the target dies" (Lux's Neutral Focus)
    // doesn't have to wait out the rest of Duration. Filtered on PriorityTarget, so this costs nothing
    // for anyone who has never had one set.
    [Preserve]
    public unsafe class PriorityTargetSystem : SystemMainThreadFilter<PriorityTargetSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Priority->Remaining <= FP._0)
                return;

            filter.Priority->Remaining -= f.DeltaTime;

            bool expired = filter.Priority->Remaining <= FP._0;
            bool targetInvalid = IsTargetInvalid(f, filter.Priority->Target);

            if (expired == false && targetInvalid == false)
                return;

            EntityRef clearedTarget = filter.Priority->Target;

            filter.Priority->Remaining = FP._0;
            filter.Priority->Target = EntityRef.None;

            f.Events.PriorityTargetCleared(filter.Entity, clearedTarget);
        }

        private static bool IsTargetInvalid(Frame f, EntityRef target)
        {
            if (target == EntityRef.None)
                return true;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return true;

            return enemy->Phase == EnemyActionPhase.Dead;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PriorityTarget* Priority;
        }
    }
}
