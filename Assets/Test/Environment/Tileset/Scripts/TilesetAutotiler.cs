using System.Collections.Generic;
using UnityEngine;

// Pure top-down autotile solver for one platform (a 4-connected set of grid cells, x = +X east,
// y = +Z north). Height is not a cell dimension - every piece is later scaled to the cube height.
// Two modes (chosen per TilesetDefinition):
//
// DualGrid: tiles sit on GRID VERTICES (half a cell off the cube grid) and cover the 4 quarter
// cells around them, so only 4 shapes exist - Center (4 quarters solid), Edge (2 adjacent),
// Corner (1) and InnerCorner (3) - and every layout is those rotated (2 diagonal quarters = two
// Corners). No fallbacks, no 2x2 special case: the concave fillet lives inside one InnerCorner tile.
//
// PerCell: each cell gets a config: which of its 4 sides are exposed (no solid neighbour) plus which of its
// 4 vertices are concave (both adjacent sides covered but the diagonal cell empty). The 15 cell
// pieces in CellPieces cover every possible config under 90deg rotation (verified by the Blender
// generator), so any layout tiles without gaps. The 2x2 InnerCorner (fillet bulging into the empty
// diagonal cell, occupying the 3 solid cells of the L) is placed first wherever its three cells
// match exactly; everything else falls back to per-cell pieces with a sharp inner corner.
public static class TilesetAutotiler
{
    public const int SideN = 1, SideE = 2, SideS = 4, SideW = 8;
    public const int VertNE = 1, VertSE = 2, VertSW = 4, VertNW = 8;
    public const string InnerCornerKey = "InnerCorner";

    public readonly struct CellPiece
    {
        public readonly string Key;
        public readonly int Sides;
        public readonly int Verts;

        public CellPiece(string key, int sides, int verts)
        {
            Key = key;
            Sides = sides;
            Verts = verts;
        }
    }

    // Canonical orientation of every model (must match build_outpost_autotiles.py CELL_PIECES).
    public static readonly CellPiece[] CellPieces =
    {
        new("Center", 0, 0),
        new("Center_1Inner", 0, VertNE),
        new("Center_2InnerAdj", 0, VertNE | VertSE),
        new("Center_2InnerDiag", 0, VertNE | VertSW),
        new("Center_3Inner", 0, VertNE | VertSE | VertSW),
        new("Center_4Inner", 0, 15),
        new("Edge", SideS, 0),
        new("Edge_InnerNE", SideS, VertNE),
        new("Edge_InnerNW", SideS, VertNW),
        new("Edge_InnerBoth", SideS, VertNE | VertNW),
        new("Corner", SideS | SideW, 0),
        new("Corner_Inner", SideS | SideW, VertNE),
        new("Corridor", SideN | SideS, 0),
        new("End", SideE | SideS | SideW, 0),
        new("Single", 15, 0),
    };

    public enum Mode
    {
        PerCell,
        DualGrid,
    }

    public const string CenterKey = "Center";
    public const string EdgeKey = "Edge";
    public const string CornerKey = "Corner";
    // Optional 2-long straight wall (DualGrid), used for Edge run segments of 2+ cells (see MergeEdges).
    public const string EdgeLongKey = "Edge2";

    // Dual-grid quarter mask around a vertex, clockwise so RotateMask also rotates it: NE, SE, SW, NW.
    public const int QuarterNE = 1, QuarterSE = 2, QuarterSW = 4, QuarterNW = 8;

    // Canonical orientation of the dual-grid models (must match build_grasscliff_autotiles.py).
    public static readonly (string key, int mask)[] DualPieces =
    {
        (CenterKey, 15),
        (EdgeKey, QuarterNE | QuarterNW),                  // north half solid, wall faces -Z
        (CornerKey, QuarterNE),                            // NE quarter solid
        (InnerCornerKey, QuarterNE | QuarterSE | QuarterNW), // SW quarter empty
    };

    public struct Placement
    {
        public string Key;
        // Pivot in grid units on the XZ plane: cell center (PerCell pieces), grid vertex (InnerCorner
        // in PerCell mode and every DualGrid tile), or the middle of a merged Center rectangle.
        public Vector2 Position;
        // Number of 90deg clockwise (seen from above, = +Y in Unity) steps.
        public int Rotation;
        // Footprint in tiles (> 1 only for merged Centers).
        public Vector2Int Size;
        // Along-wall stretch of an Edge/Edge2 covering more cells than its native length (0 = 1).
        public float Stretch;
        // Stable per-tile coordinate used to pick a variant.
        public Vector2Int Seed;
    }

    // Rotates a side/vertex mask one 90deg clockwise step: N->E->S->W, NE->SE->SW->NW.
    public static int RotateMask(int mask) => ((mask << 1) | (mask >> 3)) & 15;

    public static List<Placement> Solve(HashSet<Vector2Int> cells, Mode mode, bool useInnerCorners = true, bool mergeCenters = true, EdgeRunOptions edgeRuns = default)
    {
        var placements = mode == Mode.DualGrid ? SolveDualGrid(cells) : SolvePerCell(cells, useInnerCorners);
        if (edgeRuns.Enabled && mode == Mode.DualGrid)
            placements = MergeEdges(placements, edgeRuns);
        return mergeCenters ? MergeCenters(placements) : placements;
    }

    // Direction a canonical Edge runs along (+X) after `rotation` clockwise steps: +X, -Z, -X, +Z.
    private static Vector2Int EdgeDirection(int rotation)
    {
        switch (rotation & 3)
        {
            case 0: return new Vector2Int(1, 0);
            case 1: return new Vector2Int(0, -1);
            case 2: return new Vector2Int(-1, 0);
            default: return new Vector2Int(0, 1);
        }
    }

    // How straight Edge runs are split (DualGrid). Every run is cut into segments of 1..MaxLength
    // cells; each segment is ONE edge model stretched along the wall to cover it (the cells it covers
    // are skipped), so walls get pieces of different lengths instead of a regular 1-unit rhythm.
    // A segment of L cells uses Edge2 (native 2 long) when HasLong and L >= 2, else Edge (native 1);
    // L is only allowed if L / native <= MaxStretch. The split is a stable hash of the run.
    public struct EdgeRunOptions
    {
        public bool HasLong;
        public int MaxLength;
        public float MaxStretch;

        public bool Enabled => HasLong || (MaxLength > 1 && MaxStretch > 1f);
    }

    // Weight of picking a segment of this many cells (index = length); longer pieces preferred.
    private static readonly float[] SegmentWeights = { 0f, 0.2f, 1f, 0.8f, 0.4f, 0.25f, 0.15f };

    public static List<Placement> MergeEdges(List<Placement> placements, EdgeRunOptions options)
    {
        var result = new List<Placement>(placements.Count);
        var edges = new Dictionary<(Vector2Int, int), Placement>();
        foreach (var p in placements)
        {
            if (p.Key == EdgeKey)
                edges[(Vector2Int.RoundToInt(p.Position), p.Rotation)] = p;
            else
                result.Add(p);
        }

        var run = new List<Vector2Int>();
        foreach (var ((vertex, rotation), _) in edges)
        {
            var dir = EdgeDirection(rotation);
            if (edges.ContainsKey((vertex - dir, rotation)))
                continue; // not the start of its run

            run.Clear();
            for (var v = vertex; edges.ContainsKey((v, rotation)); v += dir)
                run.Add(v);

            var rng = (uint)(vertex.x * 73856093 ^ vertex.y * 19349663 ^ rotation * 83492791) | 1u;
            for (var i = 0; i < run.Count;)
            {
                var length = PickSegment(run.Count - i, options, ref rng);
                var native = options.HasLong && length >= 2 ? 2 : 1;
                result.Add(new Placement
                {
                    Key = native == 2 ? EdgeLongKey : EdgeKey,
                    // segment covers vertices i .. i+length-1 -> pivot in the middle
                    Position = (Vector2)(run[i] + run[i + length - 1]) * 0.5f,
                    Rotation = rotation,
                    Size = Vector2Int.one,
                    Stretch = length / (float)native,
                    Seed = run[i],
                });
                i += length;
            }
        }

        return result;
    }

    private static int PickSegment(int remaining, EdgeRunOptions o, ref uint rng)
    {
        var total = 0f;
        var max = Mathf.Min(remaining, Mathf.Min(o.MaxLength, SegmentWeights.Length - 1));
        for (var l = 1; l <= max; l++)
            total += Allowed(l, o) ? SegmentWeights[l] : 0f;

        // xorshift32
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        var roll = (rng & 0xffffff) / (float)0x1000000 * total;
        for (var l = max; l >= 1; l--)
        {
            if (!Allowed(l, o))
                continue;
            roll -= SegmentWeights[l];
            if (roll <= 0f)
                return l;
        }

        return 1;
    }

    private static bool Allowed(int length, EdgeRunOptions o)
    {
        var native = o.HasLong && length >= 2 ? 2 : 1;
        return length == 1 || length / (float)native <= o.MaxStretch + 1e-4f;
    }

    public static List<Placement> SolveDualGrid(HashSet<Vector2Int> cells)
    {
        var placements = new List<Placement>(cells.Count * 2);
        var vertices = new HashSet<Vector2Int>();
        foreach (var c in cells)
        {
            vertices.Add(c);
            vertices.Add(new Vector2Int(c.x + 1, c.y));
            vertices.Add(new Vector2Int(c.x, c.y + 1));
            vertices.Add(new Vector2Int(c.x + 1, c.y + 1));
        }

        foreach (var v in vertices)
        {
            // Cell (x, y) spans [x, x+1] x [y, y+1], so the cell NE of vertex v is v itself.
            var mask = 0;
            if (cells.Contains(v)) mask |= QuarterNE;
            if (cells.Contains(new Vector2Int(v.x, v.y - 1))) mask |= QuarterSE;
            if (cells.Contains(new Vector2Int(v.x - 1, v.y - 1))) mask |= QuarterSW;
            if (cells.Contains(new Vector2Int(v.x - 1, v.y))) mask |= QuarterNW;

            if (mask == (QuarterNE | QuarterSW) || mask == (QuarterSE | QuarterNW))
            {
                // Diagonal: two convex corners back to back.
                AddDual(placements, CornerKey, v, mask == (QuarterNE | QuarterSW) ? 0 : 1);
                AddDual(placements, CornerKey, v, mask == (QuarterNE | QuarterSW) ? 2 : 3);
                continue;
            }

            if (TryMatchDual(mask, out var key, out var rotation))
                AddDual(placements, key, v, rotation);
        }

        return placements;
    }

    private static bool TryMatchDual(int mask, out string key, out int rotation)
    {
        foreach (var (pieceKey, canonical) in DualPieces)
        {
            var m = canonical;
            for (var k = 0; k < 4; k++, m = RotateMask(m))
            {
                if (m != mask)
                    continue;
                key = pieceKey;
                rotation = k;
                return true;
            }
        }

        key = null;
        rotation = 0;
        return false;
    }

    private static void AddDual(List<Placement> placements, string key, Vector2Int vertex, int rotation)
    {
        placements.Add(new Placement { Key = key, Position = vertex, Rotation = rotation, Size = Vector2Int.one, Seed = vertex });
    }

    // Greedy rectangles over unit Center tiles (same Y) so one stretched Center replaces many.
    public static List<Placement> MergeCenters(List<Placement> placements)
    {
        var result = new List<Placement>(placements.Count);
        var centers = new Dictionary<Vector2Int, Placement>();
        foreach (var p in placements)
        {
            if (p.Key == CenterKey && p.Size == Vector2Int.one)
                centers[Vector2Int.FloorToInt(p.Position)] = p;
            else
                result.Add(p);
        }

        var keys = new List<Vector2Int>(centers.Keys);
        keys.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        var used = new HashSet<Vector2Int>();
        foreach (var start in keys)
        {
            if (used.Contains(start))
                continue;

            var w = 1;
            while (centers.ContainsKey(new Vector2Int(start.x + w, start.y)) && !used.Contains(new Vector2Int(start.x + w, start.y)))
                w++;

            var h = 1;
            while (true)
            {
                var rowOk = true;
                for (var x = 0; x < w && rowOk; x++)
                {
                    var c = new Vector2Int(start.x + x, start.y + h);
                    rowOk = centers.ContainsKey(c) && !used.Contains(c);
                }

                if (!rowOk)
                    break;
                h++;
            }

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                used.Add(new Vector2Int(start.x + x, start.y + y));

            var first = centers[start];
            result.Add(new Placement
            {
                Key = CenterKey,
                Position = first.Position + new Vector2((w - 1) * 0.5f, (h - 1) * 0.5f),
                Rotation = 0,
                Size = new Vector2Int(w, h),
                Seed = start,
            });
        }

        return result;
    }

    public static List<Placement> SolvePerCell(HashSet<Vector2Int> cells, bool useInnerCorners = true)
    {
        var placements = new List<Placement>(cells.Count);
        var claimed = new HashSet<Vector2Int>();

        if (useInnerCorners)
            PlaceInnerCorners(cells, claimed, placements);

        foreach (var cell in cells)
        {
            if (claimed.Contains(cell))
                continue;

            var (sides, verts) = Config(cells, cell);
            if (TryMatch(sides, verts, out var key, out var rotation))
            {
                placements.Add(new Placement
                {
                    Key = key,
                    Position = new Vector2(cell.x + 0.5f, cell.y + 0.5f),
                    Rotation = rotation,
                    Size = Vector2Int.one,
                    Seed = cell,
                });
            }
        }

        return placements;
    }

    public static (int sides, int verts) Config(HashSet<Vector2Int> cells, Vector2Int c)
    {
        var sides = 0;
        if (!cells.Contains(new Vector2Int(c.x, c.y + 1))) sides |= SideN;
        if (!cells.Contains(new Vector2Int(c.x + 1, c.y))) sides |= SideE;
        if (!cells.Contains(new Vector2Int(c.x, c.y - 1))) sides |= SideS;
        if (!cells.Contains(new Vector2Int(c.x - 1, c.y))) sides |= SideW;

        var verts = 0;
        if ((sides & (SideN | SideE)) == 0 && !cells.Contains(new Vector2Int(c.x + 1, c.y + 1))) verts |= VertNE;
        if ((sides & (SideS | SideE)) == 0 && !cells.Contains(new Vector2Int(c.x + 1, c.y - 1))) verts |= VertSE;
        if ((sides & (SideS | SideW)) == 0 && !cells.Contains(new Vector2Int(c.x - 1, c.y - 1))) verts |= VertSW;
        if ((sides & (SideN | SideW)) == 0 && !cells.Contains(new Vector2Int(c.x - 1, c.y + 1))) verts |= VertNW;
        return (sides, verts);
    }

    public static bool TryMatch(int sides, int verts, out string key, out int rotation)
    {
        foreach (var piece in CellPieces)
        {
            int s = piece.Sides, v = piece.Verts;
            for (var k = 0; k < 4; k++)
            {
                if (s == sides && v == verts)
                {
                    key = piece.Key;
                    rotation = k;
                    return true;
                }

                s = RotateMask(s);
                v = RotateMask(v);
            }
        }

        key = null;
        rotation = 0;
        return false;
    }

    // Canonical InnerCorner (rotation 0): concave vertex at the origin, empty cell SW of it.
    // Cell offsets are relative to the vertex (cell (x, y) spans [x, x+1] x [y, y+1]).
    private static readonly (Vector2Int offset, int sides, int verts)[] InnerCornerCells =
    {
        (new Vector2Int(0, 0), 0, VertSW), // elbow (diagonal to the empty cell)
        (new Vector2Int(0, -1), SideW, 0), // arm facing the empty cell from the east
        (new Vector2Int(-1, 0), SideS, 0), // arm facing the empty cell from the north
    };

    private static void PlaceInnerCorners(HashSet<Vector2Int> cells, HashSet<Vector2Int> claimed, List<Placement> placements)
    {
        // Every concave grid vertex, in a stable order so rebuilds are deterministic.
        var vertices = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)));
        foreach (var c in cells)
        {
            var (_, verts) = Config(cells, c);
            if ((verts & VertNE) != 0) vertices.Add(new Vector2Int(c.x + 1, c.y + 1));
            if ((verts & VertSE) != 0) vertices.Add(new Vector2Int(c.x + 1, c.y));
            if ((verts & VertSW) != 0) vertices.Add(new Vector2Int(c.x, c.y));
            if ((verts & VertNW) != 0) vertices.Add(new Vector2Int(c.x, c.y + 1));
        }

        var picked = new Vector2Int[3];
        foreach (var v in vertices)
        {
            // Which quadrant around the vertex is empty decides the rotation (SW=0, NW=1, NE=2, SE=3).
            int rotation;
            if (!cells.Contains(new Vector2Int(v.x - 1, v.y - 1))) rotation = 0;
            else if (!cells.Contains(new Vector2Int(v.x - 1, v.y))) rotation = 1;
            else if (!cells.Contains(new Vector2Int(v.x, v.y))) rotation = 2;
            else rotation = 3;

            var ok = true;
            for (var i = 0; i < InnerCornerCells.Length && ok; i++)
            {
                var (offset, sides, verts) = InnerCornerCells[i];
                for (var k = 0; k < rotation; k++)
                {
                    offset = new Vector2Int(offset.y, -offset.x - 1); // rotate a cell 90deg CW around the vertex
                    sides = RotateMask(sides);
                    verts = RotateMask(verts);
                }

                var cell = v + offset;
                ok = cells.Contains(cell) && !claimed.Contains(cell) && Config(cells, cell) == (sides, verts);
                picked[i] = cell;
            }

            if (!ok)
                continue;

            foreach (var cell in picked)
                claimed.Add(cell);

            placements.Add(new Placement { Key = InnerCornerKey, Position = v, Rotation = rotation, Size = Vector2Int.one, Seed = v });
        }
    }
}
