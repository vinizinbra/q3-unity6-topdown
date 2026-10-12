namespace Quantum
{
    using Photon.Deterministic;

    // Shared "find a safe respawn point near this position" logic - originally PlayerFallSystem's
    // own private helpers, extracted so EnemyFallSystem (Boss/Elite tier only, see its own comment)
    // can reuse the exact same nearest-chunk/inset-into-bounds math instead of re-deriving it.
    // Position-based rather than tied to any tracked "last grounded" field, so it works equally for
    // a player's own PlayerMovement.LastGroundedPosition and an enemy's current (mid-fall)
    // Transform3D.Position - enemies don't track a last-grounded position of their own.
    public static unsafe class FallRespawnUtility
    {
        // Prefers a chunk's own hand-authored RespawnPoint (see ChunkRespawnPointBaker) when one
        // is baked - typically a Traversal chunk, whose open drops are often in its own interior,
        // not just its outer boundary, so an automatic fallback can't reliably reason about them.
        // Otherwise insets fromPosition away from that chunk's own footprint boundary, since
        // "where the fall started" is often right at the edge something walked off. Uses the
        // NEAREST chunk rather than requiring strict containment - a fall most often happens right
        // at a seam between two adjacent chunks (or past the outer edge of the generated level),
        // which can land just outside every chunk's own strict bounds.
        public static FPVector3 ResolveNearestChunkRespawnPosition(Frame f, FPVector3 fromPosition, LevelConfig config)
        {
            FPVector3 groundPosition = fromPosition;

            if (TryFindNearestChunk(f, fromPosition, out EntityRef chunkEntity))
            {
                Chunk* chunk = f.Unsafe.GetPointer<Chunk>(chunkEntity);
                Transform3D* chunkTransform = f.Unsafe.GetPointer<Transform3D>(chunkEntity);

                groundPosition = chunk->HasRespawnPoint
                    ? chunkTransform->Position + chunkTransform->Rotation * chunk->RespawnPoint
                    : InsetIntoChunkBounds(fromPosition, chunk, chunkTransform, config.FallRespawnEdgeMargin);
            }

            return groundPosition + FPVector3.Up * config.FallRespawnHeightOffset;
        }

        // Where an enemy that fell off the level (EnemySystem.CheckFallDeath kills it below
        // LevelConfig.FallDeathHeight) should leave its drops - the same nearest-chunk point a
        // falling Elite/player respawns at, snapped to the real floor. False (dropPosition =
        // position, unchanged) when position isn't below FallDeathHeight, i.e. a normal death.
        // The raycast is lifted to at least the chunk's own origin height first - the inset
        // position still carries the deep mid-fall Y, which can sit out of the ground probe's reach.
        public static bool TryResolveFallDropPosition(Frame f, FPVector3 position, out FPVector3 dropPosition)
        {
            dropPosition = position;

            LevelConfig config = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (config == null || position.Y >= config.FallDeathHeight)
                return false;

            FPVector3 anchor = ResolveNearestChunkRespawnPosition(f, position, config);

            if (TryFindNearestChunk(f, position, out EntityRef chunkEntity))
                anchor.Y = FPMath.Max(anchor.Y, f.Unsafe.GetPointer<Transform3D>(chunkEntity)->Position.Y);

            if (EnemyMovementUtility.TryFindGroundHeight(f, anchor, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY) == true)
                anchor.Y = groundY;

            dropPosition = anchor;
            return true;
        }

        // Nearest chunk by closest-point-on-AABB distance, not strict containment - naturally
        // resolves to the actual containing chunk (distance 0) whenever position genuinely is
        // inside one, and degrades gracefully to whichever chunk's boundary is closest otherwise
        // (a seam gap, or genuinely outside every placed chunk). Always finds a chunk as long as
        // at least one Chunk entity exists, which is always true for a generated level - only run
        // reactively on the actual fall event, not per-tick, so an O(n) scan over every chunk is
        // cheap enough with no caching needed.
        public static bool TryFindNearestChunk(Frame f, FPVector3 position, out EntityRef chunkEntity)
        {
            chunkEntity = EntityRef.None;
            FP bestSqrDistance = FP.MaxValue;

            var filtered = f.Filter<Chunk, Transform3D>();

            while (filtered.Next(out EntityRef entity, out Chunk chunk, out Transform3D transform))
            {
                FPVector3 local = FPQuaternion.Inverse(transform.Rotation) * (position - transform.Position);
                local.X = FPMath.Clamp(local.X, FP._0, chunk.ChunkSizeWidth);
                local.Z = FPMath.Clamp(local.Z, FP._0, chunk.ChunkSizeDepth);

                FPVector3 closestPoint = transform.Position + transform.Rotation * local;
                FP sqrDistance = (closestPoint - position).SqrMagnitude;

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    chunkEntity = entity;
                }
            }

            return chunkEntity != EntityRef.None;
        }

        // True when position falls within chunkEntity's own footprint - same Position-as-min-corner,
        // inverse-rotation local-space transform TryFindNearestChunk/InsetIntoChunkBounds already
        // use below, just a plain containment test with no clamping. Used by AccessoryGuardUtility
        // to keep a dropped accessory's landing spot inside the sealed Boss Arena chunk rather than
        // past its own BossArenaGate walls (see ResolveLandingPosition's own comment).
        public static bool IsInsideChunkBounds(Frame f, EntityRef chunkEntity, FPVector3 position)
        {
            Chunk* chunk = f.Unsafe.GetPointer<Chunk>(chunkEntity);
            Transform3D* chunkTransform = f.Unsafe.GetPointer<Transform3D>(chunkEntity);

            FPVector3 local = FPQuaternion.Inverse(chunkTransform->Rotation) * (position - chunkTransform->Position);

            return local.X >= FP._0 && local.X <= chunk->ChunkSizeWidth
                && local.Z >= FP._0 && local.Z <= chunk->ChunkSizeDepth;
        }

        // Clamps position into chunk's own local footprint, margin units away from every side -
        // caps the margin at half the chunk's own size first, so a chunk smaller than 2x margin
        // degrades to its center instead of inverting the clamp range. Works whether position is
        // actually inside the chunk or (via TryFindNearestChunk) just outside it - either way the
        // clamp pulls it to a point margin units inside this chunk's own bounds.
        private static FPVector3 InsetIntoChunkBounds(FPVector3 position, Chunk* chunk, Transform3D* chunkTransform, FP margin)
        {
            FPVector3 local = FPQuaternion.Inverse(chunkTransform->Rotation) * (position - chunkTransform->Position);

            FP widthMargin = FPMath.Min(margin, chunk->ChunkSizeWidth * FP._0_50);
            FP depthMargin = FPMath.Min(margin, chunk->ChunkSizeDepth * FP._0_50);

            local.X = FPMath.Clamp(local.X, widthMargin, chunk->ChunkSizeWidth - widthMargin);
            local.Z = FPMath.Clamp(local.Z, depthMargin, chunk->ChunkSizeDepth - depthMargin);

            return chunkTransform->Position + chunkTransform->Rotation * local;
        }
    }
}
