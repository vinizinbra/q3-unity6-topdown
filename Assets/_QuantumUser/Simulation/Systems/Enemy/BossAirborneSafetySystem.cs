namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Safety net for the in-delivery flight states (SwoopState / AirborneKneelState): if the entity is no longer
    // running the Active delivery that owns that state (killed mid-air, despawned into another phase, any path that
    // skipped OnInterrupted), it's put back on a valid floor with normal physics and the state is dropped - never
    // left hanging in the air or with an orphaned delivery state. Registered right after BossSystem.
    [Preserve]
    public unsafe class BossAirborneSafetySystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            List<EntityRef> orphans = null;

            var swoops = f.Filter<Enemy, SwoopState>();
            while (swoops.NextUnsafe(out EntityRef entity, out Enemy* enemy, out SwoopState* _))
            {
                if (IsRunning<SwoopDeliveryData>(f, enemy) == false)
                    (orphans ??= new List<EntityRef>()).Add(entity);
            }

            var kneels = f.Filter<Enemy, AirborneKneelState>();
            while (kneels.NextUnsafe(out EntityRef entity, out Enemy* enemy, out AirborneKneelState* _))
            {
                if (IsRunning<AirborneKneelDeliveryData>(f, enemy) == false)
                    (orphans ??= new List<EntityRef>()).Add(entity);
            }

            if (orphans == null)
                return;

            for (int i = 0; i < orphans.Count; i++)
                GroundNow(f, orphans[i]);
        }

        private static bool IsRunning<T>(Frame f, Enemy* enemy) where T : EnemyDeliveryData
        {
            if (enemy->Phase != EnemyActionPhase.Active)
                return false;

            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, f.FindAsset(enemy->EnemyData), enemy->CurrentActionSlot);
            return action != null && f.FindAsset(action.Delivery) is T;
        }

        // Drops any in-delivery flight state and puts the entity on the floor below it (or the arena spawn point).
        // Also called by BossDefeatUtility.TryBegin, whose hard pause would otherwise freeze a boss killed mid-air.
        public static void GroundNow(Frame f, EntityRef entity)
        {
            bool hadState = false;

            if (f.Has<SwoopState>(entity) == true)
            {
                f.Remove<SwoopState>(entity);
                hadState = true;
            }

            if (f.Has<AirborneKneelState>(entity) == true)
            {
                f.Remove<AirborneKneelState>(entity);
                hadState = true;
            }

            if (hadState == false || f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            DeliveryStageUtility.Clear(f, entity);

            FP pivot = EnemyMovementUtility.ResolveEntityRadius(f, entity);
            transform->Position = AirborneKneelDeliveryData.ResolveLanding(f, entity, transform->Position, pivot);

            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(entity, out var body) == true)
            {
                body->Velocity = FPVector3.Zero;

                if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == true && enemy->Phase != EnemyActionPhase.Dead)
                    body->IsKinematic = false;
            }
        }
    }
}
