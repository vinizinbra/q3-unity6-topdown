namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks each Active Traversal Challenge's own countdown/checkpoint check (see
    // TraversalChallenge.qtn/docs/traversal-challenge.md). Lives inside GameplaySystemGroup (unlike
    // BossPauseSystem/ChestSystem/LevelUpSystem) since it never itself disables/enables that group -
    // this POI deliberately never pauses player input, only the global SurvivalTime/enemy-spawn
    // pair (via Global.ActiveTraversalChallengeCount, checked from SurvivalProgressionUtility/
    // CombatDirectorSystem).
    [Preserve]
    public unsafe class TraversalChallengeSystem : SystemMainThreadFilter<TraversalChallengeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.TraversalChallenge->State != TraversalChallengeState.Active)
                return;

            // Chunk-relative, not activator-relative - same cached anchor TryActivate resolved its
            // platform spawn positions from (see TraversalChallenge.Chunk's own comment).
            TraversalChallengeUtility.ResolveCachedAnchor(f, filter.Entity, filter.TraversalChallenge, out FPVector3 anchorPosition, out FPQuaternion anchorRotation);
            FPVector3 checkpoint = anchorPosition + anchorRotation * filter.TraversalChallenge->CheckpointPosition;
            FP radius = filter.TraversalChallenge->CheckpointRadius;

            // Same proximity idiom ChestSystem.Update already uses - PlayerQueryUtility's own
            // collider-radius-inclusive range test first, then a re-checked exact SqrMagnitude
            // guard. First player found inside the radius completes it for everyone.
            Span<EntityRef> hits = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int hitsCount = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(f, checkpoint, radius, hits);
            for (int i = 0; i < hitsCount; i++)
            {
                if (f.Unsafe.TryGetPointer<Transform3D>(hits[i], out var playerTransform) == false)
                    continue;

                if ((playerTransform->Position - checkpoint).SqrMagnitude > radius * radius)
                    continue;

                TraversalChallengeUtility.Complete(f, filter.Entity, filter.TraversalChallenge);
                return;
            }

            // Cheap client-facing convenience value for the global HUD banner - see
            // Global.TraversalChallengeTimeRemaining's own comment.
            f.Global->TraversalChallengeTimeRemaining = filter.TraversalChallenge->RemainingTime;

            filter.TraversalChallenge->RemainingTime -= f.DeltaTime;

            if (filter.TraversalChallenge->RemainingTime <= FP._0)
            {
                TraversalChallengeUtility.Fail(f, filter.Entity, filter.TraversalChallenge);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public TraversalChallenge* TraversalChallenge;
            public Transform3D* Transform3D;
        }
    }
}
