namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Marks Chunk.Discovered true the first time any player's approximate camera view (the
    // OverallGameConfig.DiscoveryView* rect around them) overlaps that chunk's own world footprint -
    // shared/co-op, not per-player (see docs/minimap.md). The real camera is per-client View state,
    // so the sim uses this fixed rect as a deterministic stand-in. Drives the minimap's
    // "?" -> real icon reveal. Already-discovered chunks are skipped, so the per-tick cost only
    // ever covers the shrinking set of chunks nobody has reached yet. No ordering dependency on
    // anything else in GameplaySystemGroup - reads whichever Transform3D positions are already
    // resolved this tick, same "fine to be a tick stale" reasoning CombatDirectorSystem's own
    // comment gives for its moving-bubble check.
    [Preserve]
    public unsafe class ChunkDiscoverySystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            OverallGameConfig config = f.GameConfig;
            var chunks = f.Filter<Chunk, Transform3D>();

            while (chunks.Next(out EntityRef chunkEntity, out Chunk chunk, out Transform3D chunkTransform))
            {
                if (chunk.Discovered)
                    continue;

                // Chunks are min-corner pivoted and never rotated, so Transform3D.Position IS the
                // footprint's world min corner. ChunkSizeWidth/Depth are already world-space units,
                // not grid-cell counts (see Chunk.qtn's own comment), so no CellSize conversion is
                // needed here and they map straight to world X/Z.
                FPVector3 min = chunkTransform.Position;
                FPVector3 max = min + new FPVector3(chunk.ChunkSizeWidth, FP._0, chunk.ChunkSizeDepth);

                if (IsAnyPlayerViewing(f, config, min, max) == false)
                    continue;

                f.Unsafe.GetPointer<Chunk>(chunkEntity)->Discovered = true;
            }
        }

        // X/Z only - Y (height) isn't part of a chunk's footprint, same convention
        // LobbyBoundarySystem's own IsInsideFootprint uses. Camera looks toward +Z, so the view rect
        // reaches further forward than back. A chunk counts as seen once one player stands inside it,
        // or one player's view rect covers at least DiscoveryMinVisibleFraction of the smaller of
        // (chunk area, view area) - measured against the chunk alone, a chunk bigger than the view
        // could stay undiscovered with a player standing right in it. Per player, not the union of
        // several players' views - fine for a "roughly on screen" reveal.
        private static bool IsAnyPlayerViewing(Frame f, OverallGameConfig config, FPVector3 min, FPVector3 max)
        {
            FP chunkArea = (max.X - min.X) * (max.Z - min.Z);
            FP viewArea = config.DiscoveryViewHalfWidth * 2 * (config.DiscoveryViewBack + config.DiscoveryViewForward);
            FP requiredArea = FPMath.Min(chunkArea, viewArea) * config.DiscoveryMinVisibleFraction;
            var players = f.Filter<PlayerLink, Transform3D>();

            while (players.Next(out EntityRef _, out PlayerLink _, out Transform3D playerTransform))
            {
                FPVector3 position = playerTransform.Position;

                if (position.X >= min.X && position.X <= max.X && position.Z >= min.Z && position.Z <= max.Z)
                    return true;

                FP overlapX = FPMath.Min(position.X + config.DiscoveryViewHalfWidth, max.X) - FPMath.Max(position.X - config.DiscoveryViewHalfWidth, min.X);
                FP overlapZ = FPMath.Min(position.Z + config.DiscoveryViewForward, max.Z) - FPMath.Max(position.Z - config.DiscoveryViewBack, min.Z);

                if (overlapX > FP._0 && overlapZ > FP._0 && overlapX * overlapZ >= requiredArea)
                    return true;
            }

            return false;
        }
    }
}
