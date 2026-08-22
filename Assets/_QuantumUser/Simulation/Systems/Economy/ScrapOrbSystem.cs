namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a ScrapOrb once the Lux who owns it (the LuxScrapCollector holder ScrapUtility.
    // TrySpawnDrop rolled the drop for) walks within pickup range. Unlike CurrencyOrbSystem, this never
    // checks every nearby player - Scrap only ever means anything to whoever has the passive, so a
    // candidate without LuxScrapCollector is simply skipped. The padded broadphase prefilter this
    // used to share with CurrencyOrbSystem is gone - see that system's own comment.
    [Preserve]
    public unsafe class ScrapOrbSystem : SystemMainThreadFilter<ScrapOrbSystem.Filter>, ISignalOnFreeCastUsed
    {
        public override void Update(Frame f, ref Filter filter)
        {
            FP pickupRadius = FP._2;

            if (f.RuntimeConfig.ScrapConfig.IsValid == true)
            {
                pickupRadius = f.FindAsset(f.RuntimeConfig.ScrapConfig).PickupRadius;
            }

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int playerCount = PlayerQueryUtility.GatherPlayers(f, players);

            EntityRef collectorEntity = EntityRef.None;
            LuxScrapCollector* collectorData = null;
            FP closestSqrDistance = default;

            for (int i = 0; i < playerCount; i++)
            {
                EntityRef player = players[i];

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

                // Nearest wins - see CurrencyOrbSystem's own note on this.
                if (collectorEntity != EntityRef.None && sqrDistance >= closestSqrDistance)
                    continue;

                collectorEntity = player;
                collectorData = collector;
                closestSqrDistance = sqrDistance;
            }

            if (collectorEntity == EntityRef.None)
                return;

            ScrapUtility.Grant(f, collectorEntity, collectorData);
            f.Events.ScrapOrbCollected(collectorEntity, filter.Transform3D->Position);
            f.Destroy(filter.Entity);
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
