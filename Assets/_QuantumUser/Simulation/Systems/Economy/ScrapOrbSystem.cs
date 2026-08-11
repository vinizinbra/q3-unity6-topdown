namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a ScrapOrb once the Lux who owns it (the LuxScrapCollector holder ScrapUtility.
    // TrySpawnDrop rolled the drop for) walks within pickup range. Unlike CurrencyOrbSystem, this never
    // checks every nearby player - Scrap only ever means anything to whoever has the passive, so a
    // hit without LuxScrapCollector is simply skipped, same query broadened generously the same way
    // CurrencyOrbSystem's own QueryRadiusScale is (a known simplification - see docs/experience-drops.md).
    [Preserve]
    public unsafe class ScrapOrbSystem : SystemMainThreadFilter<ScrapOrbSystem.Filter>, ISignalOnFreeCastUsed
    {
        private static readonly FP QueryRadiusScale = 8;

        public override void Update(Frame f, ref Filter filter)
        {
            FP pickupRadius = FP._2;

            if (f.RuntimeConfig.ScrapConfig.IsValid == true)
            {
                pickupRadius = f.FindAsset(f.RuntimeConfig.ScrapConfig).PickupRadius;
            }

            FP queryRadius = pickupRadius * QueryRadiusScale;
            var hits = EnemyMovementUtility.FindPlayersInRadiusForPickup(f, filter.Transform3D->Position, queryRadius);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef player = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<LuxScrapCollector>(player, out var collector) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                FP effectiveRadius = pickupRadius;

                if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == true)
                {
                    effectiveRadius *= stats->PickupRangeMultiplier;
                }

                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > effectiveRadius * effectiveRadius)
                    continue;

                ScrapUtility.Grant(f, player, collector);
                f.Events.ScrapOrbCollected(player, filter.Transform3D->Position);
                f.Destroy(filter.Entity);
                return;
            }
        }

        // ScrapUtility is a static utility, not a system, so it can't itself listen for the generic
        // OnFreeCastUsed signal (see CharacterSkills.qtn) - this system already owns everything
        // Scrap-related, so it's the natural place to forward the signal into
        // ScrapUtility.OnFreeCastConsumed, which resets LuxScrapCollector.ScrapStacks only now, at
        // the moment the free cast is actually spent.
        public void OnFreeCastUsed(Frame f, EntityRef entity, SkillSlotId slotId)
        {
            ScrapUtility.OnFreeCastConsumed(f, entity, slotId);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ScrapOrb* ScrapOrb;
            public Transform3D* Transform3D;
        }
    }
}
