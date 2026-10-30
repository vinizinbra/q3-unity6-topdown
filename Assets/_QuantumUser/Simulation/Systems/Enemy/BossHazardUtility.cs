namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Queries/cleanup for boss-owned hazards (BossTornado, CrowdBarrage + CrowdMark) - one place so every
    // delivery gate, the stagger break and the death sweep agree on what "this boss has a mechanic running" means.
    public static unsafe class BossHazardUtility
    {
        public static int CountTornadoes(Frame f, EntityRef owner)
        {
            int count = 0;
            var tornadoes = f.Filter<BossTornado>();
            while (tornadoes.NextUnsafe(out EntityRef _, out BossTornado* tornado))
            {
                if (tornado->Owner == owner)
                    count++;
            }

            return count;
        }

        // "A single major mechanic at a time": tornadoes alive OR a crowd barrage running.
        public static bool IsMajorMechanicActive(Frame f, EntityRef boss)
        {
            return f.Has<CrowdBarrage>(boss) == true || CountTornadoes(f, boss) > 0;
        }

        // Stagger break / cleanup: tornadoes dissipate with NO pulse, future throws and every mark this boss placed
        // are dropped, and every DissipateWithOwner hazard it spawned (the Frost Patriarch's ice trail) is removed.
        // Trash already in the air keeps its warned flight.
        public static void CancelOwnedHazards(Frame f, EntityRef boss)
        {
            List<EntityRef> doomed = null;

            var tornadoes = f.Filter<BossTornado, Transform3D>();
            while (tornadoes.NextUnsafe(out EntityRef entity, out BossTornado* tornado, out Transform3D* transform))
            {
                if (tornado->Owner != boss)
                    continue;

                f.Events.BossTornadoDissipated(entity, transform->Position);
                doomed ??= new List<EntityRef>();
                doomed.Add(entity);
            }

            var owned = f.Filter<DissipateWithOwner, AreaOwner>();
            while (owned.NextUnsafe(out EntityRef entity, out DissipateWithOwner* _, out AreaOwner* areaOwner))
            {
                if (areaOwner->Owner != boss)
                    continue;

                doomed ??= new List<EntityRef>();
                doomed.Add(entity);
            }

            if (doomed != null)
            {
                for (int i = 0; i < doomed.Count; i++)
                    f.Destroy(doomed[i]);
            }

            if (f.Has<CrowdBarrage>(boss) == true)
                f.Remove<CrowdBarrage>(boss);

            ClearMarks(f, boss);
        }

        public static void ClearMarks(Frame f, EntityRef boss)
        {
            List<EntityRef> marked = null;

            var marks = f.Filter<CrowdMark>();
            while (marks.NextUnsafe(out EntityRef entity, out CrowdMark* mark))
            {
                if (mark->Boss != boss)
                    continue;

                marked ??= new List<EntityRef>();
                marked.Add(entity);
            }

            if (marked == null)
                return;

            for (int i = 0; i < marked.Count; i++)
                f.Remove<CrowdMark>(marked[i]);
        }

        public static bool IsOwnerAlive(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<Enemy>(owner, out var enemy) == true && enemy->Phase != EnemyActionPhase.Dead;
        }

        // Every living player (PlayerLink, not Downed/KO) whose FLOOR is within maxHeightDifference of centerGroundY
        // and whose flat distance to center is within radius (+ its own collider). Uses the enemy-attack player mask
        // (dash i-frames apply, same as every other enemy attack). Sentries (no PlayerLink) are skipped.
        public static int GatherPlayersOnFloor(Frame f, FPVector3 center, FP centerGroundY, FP radius, FP maxHeightDifference, Span<EntityRef> output)
        {
            Span<EntityRef> candidates = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int candidateCount = EnemyMovementUtility.FindPlayersInRadius(f, center, radius + 4, candidates);
            int count = 0;

            for (int i = 0; i < candidateCount && count < output.Length; i++)
            {
                EntityRef player = candidates[i];

                if (f.Has<PlayerLink>(player) == false || PlayerLifeStateUtility.IsIncapacitated(f, player) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == false)
                    continue;

                if (EnemyMovementUtility.IsWithinFlatGroundArea(f, center, centerGroundY, transform->Position, radius, maxHeightDifference) == false)
                    continue;

                output[count++] = player;
            }

            return count;
        }
    }
}
