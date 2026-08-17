namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Resolves each player's own ContextInteraction fresh every tick - the generic half of the
    // Base-Skill-button redirect (see ContextInteraction.qtn/docs/breathing-poi.md). Registered
    // immediately before SkillSystem (inside GameplaySystemGroup), after KCCSystem, so it reads
    // this tick's already-resolved position, not last tick's. A future second interactable POI
    // kind only needs its own State-resolve switch case here - target RESOLUTION (closest-in-
    // radius) stays fully generic and, deliberately, does NOT filter by eligibility - the
    // world-space prompt widget needs to know about a nearby-but-not-usable POI too (e.g. to show
    // "come back on Break"), not just a fully-eligible one.
    [Preserve]
    public unsafe class ContextInteractionSystem : SystemMainThreadFilter<ContextInteractionSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            f.AddOrGet<ContextInteraction>(filter.Entity, out var context);

            EntityRef best = EntityRef.None;
            InteractableKind bestKind = default;
            FP bestSqrDistance = FP._0;
            int bestPriority = 0;

            var filtered = f.Filter<Interactable, Transform3D>();

            while (filtered.Next(out EntityRef candidate, out Interactable interactable, out Transform3D candidateTransform))
            {
                if (interactable.Radius <= FP._0)
                    continue;

                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(filter.Transform3D->Position, candidateTransform.Position);

                if (sqrDistance > interactable.Radius * interactable.Radius)
                    continue;

                // Closest wins first; Priority only breaks an exact distance tie; beyond that the
                // filter's own deterministic enumeration order is the final tie-break - same "no
                // dedicated EntityRef compare needed" convention EnemyMovementUtility's own
                // nearest-target resolvers already rely on. Purely geometric - eligibility is
                // resolved separately below, once, for whichever candidate actually wins this scan.
                bool better = best == EntityRef.None
                    || sqrDistance < bestSqrDistance
                    || (sqrDistance == bestSqrDistance && interactable.Priority > bestPriority);

                if (better == false)
                    continue;

                best = candidate;
                bestKind = interactable.Kind;
                bestSqrDistance = sqrDistance;
                bestPriority = interactable.Priority;
            }

            context->ActiveTarget = best;
            context->ActiveKind = bestKind;

            if (best == EntityRef.None)
            {
                context->State = ContextInteractionState.None;
                return;
            }

            // Busy is checked once here, uniformly across every InteractableKind, rather than
            // inside each kind's own resolver - already mid an interaction (with this POI, or in a
            // future multi-POI map, another) means the Base Skill button is already fully claimed
            // by that open Choice Window, regardless of what's now nearest.
            context->State = PoiInteractionLockUtility.IsInputLocked(f, filter.Entity) == true
                ? ContextInteractionState.Busy
                : ResolveState(f, filter.Entity, best, bestKind);
        }

        // The one per-kind switch this whole mechanism needs - everything else above is generic.
        private static ContextInteractionState ResolveState(Frame f, EntityRef player, EntityRef poi, InteractableKind kind)
        {
            switch (kind)
            {
                case InteractableKind.CursedRift: return CursedRiftUtility.ResolveInteractionState(f, player, poi);
                case InteractableKind.HealingShrine: return HealingShrineUtility.ResolveInteractionState(f, player, poi);
                case InteractableKind.Store: return StoreUtility.ResolveInteractionState(f, player, poi);
                case InteractableKind.Blacksmith: return BlacksmithUtility.ResolveInteractionState(f, player, poi);
                default: return ContextInteractionState.None;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
            public Transform3D* Transform3D;
        }
    }
}
