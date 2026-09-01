namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Reads the chunk adjacency graph LevelGenerationSystem.ComputeChunkConnectivity bakes onto
    // Chunk.ConnectedChunks once, at the end of procedural generation. Hero/tier-agnostic on
    // purpose (nothing here mentions Elite/Director) even though today only major-group spawning
    // calls it - see docs/survival-director.md's "Chunk Connectivity" section.
    public static unsafe class ChunkConnectivityUtility
    {
        // True if the two chunks are the same, or directly border each other. Only ever a positive
        // check - a chunk with no recorded connections (e.g. connectivity was never computed for
        // this level) simply reports false for every OTHER chunk, never true by omission.
        public static bool IsConnected(Frame f, EntityRef chunkA, EntityRef chunkB)
        {
            if (chunkA == chunkB)
            {
                return true;
            }

            if (f.Unsafe.TryGetPointer<Chunk>(chunkA, out var chunk) == false)
            {
                return false;
            }

            for (int i = 0; i < chunk->ConnectedChunkCount; i++)
            {
                if (chunk->ConnectedChunks[i] == chunkB)
                {
                    return true;
                }
            }

            return false;
        }

        // Gate for a Director spawn candidate point - permissive whenever the data needed to make a
        // real judgment is missing (no player found, or the point doesn't land inside any known
        // chunk), only rejecting on positive evidence the candidate's chunk is neither the nearest
        // player's own chunk nor directly connected to it. Matches this codebase's existing
        // forbidden-chunk-style checks (see GroupSpawnerUtility.IsInForbiddenChunk).
        public static bool IsConnectedToNearestPlayer(Frame f, FPVector3 point)
        {
            if (TryFindNearestPlayerChunk(f, point, out EntityRef playerChunk) == false)
            {
                return true;
            }

            if (EnemyPathfindingUtility.TryFindContainingChunk(f, point, out EntityRef candidateChunk) == false)
            {
                return true;
            }

            return IsConnected(f, candidateChunk, playerChunk);
        }

        private static bool TryFindNearestPlayerChunk(Frame f, FPVector3 point, out EntityRef chunkEntity)
        {
            Span<EntityRef> buffer = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int count = PlayerQueryUtility.GatherPlayers(f, buffer);

            bool found = false;
            FP bestSqrDistance = default;
            FPVector3 bestPosition = default;

            for (int i = 0; i < count; i++)
            {
                FPVector3 position = f.Unsafe.GetPointer<Transform3D>(buffer[i])->Position;
                FP sqrDistance = (position - point).SqrMagnitude;

                if (found == false || sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestPosition = position;
                    found = true;
                }
            }

            if (found == false)
            {
                chunkEntity = EntityRef.None;
                return false;
            }

            return EnemyPathfindingUtility.TryFindContainingChunk(f, bestPosition, out chunkEntity);
        }
    }
}
