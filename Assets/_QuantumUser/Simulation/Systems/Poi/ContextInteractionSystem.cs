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

            // A Downed/KO player can't resolve a normal proximity interaction - their own
            // self-revive is a separate direct check in SkillSystem, not routed through this scan
            // (see docs/revive.md). Without this, an incapacitated player's own Interactable{Kind=
            // Revive} tag (see PlayerLifeStateUtility.EnterDowned) would otherwise let them resolve
            // a "revive myself via the normal redirect" state that doesn't make sense.
            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
            {
                context->ActiveTarget = EntityRef.None;
                context->ActiveKind = default;
                context->State = ContextInteractionState.None;
                return;
            }

            EntityRef best = EntityRef.None;
            InteractableKind bestKind = default;
            FP bestSqrDistance = FP._0;
            int bestPriority = 0;
            int bestTier = 0;

            var filtered = f.Filter<Interactable, Transform3D>();

            while (filtered.Next(out EntityRef candidate, out Interactable interactable, out Transform3D candidateTransform))
            {
                // Unreachable before Revive existed (no POI ever tagged a player) - now reachable
                // since a Downed/KO player's own entity carries Interactable{Kind=Revive}.
                if (candidate == filter.Entity)
                    continue;

                if (interactable.Radius <= FP._0)
                    continue;

                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(filter.Transform3D->Position, candidateTransform.Position);

                if (sqrDistance > interactable.Radius * interactable.Radius)
                    continue;

                int tier = InteractableKindUtility.GetPriorityTier(interactable.Kind);

                // Kind tier wins first (e.g. Revive always beats an ordinary POI, regardless of
                // distance); closest wins next; Priority only breaks an exact distance tie within
                // the same tier; beyond that the filter's own deterministic enumeration order is
                // the final tie-break - same "no dedicated EntityRef compare needed" convention
                // EnemyMovementUtility's own nearest-target resolvers already rely on. Purely
                // geometric - eligibility is resolved separately below, once, for whichever
                // candidate actually wins this scan.
                bool better = best == EntityRef.None
                    || tier > bestTier
                    || (tier == bestTier && sqrDistance < bestSqrDistance)
                    || (tier == bestTier && sqrDistance == bestSqrDistance && interactable.Priority > bestPriority);

                if (better == false)
                    continue;

                best = candidate;
                bestKind = interactable.Kind;
                bestSqrDistance = sqrDistance;
                bestPriority = interactable.Priority;
                bestTier = tier;
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
                case InteractableKind.TraversalChallenge: return TraversalChallengeUtility.ResolveInteractionState(f, player, poi);
                case InteractableKind.Revive: return ReviveUtility.ResolveInteractionState(f, player, poi);
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
