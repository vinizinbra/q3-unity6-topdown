using System.Collections.Generic;
using UnityEngine;

// Visual-only things floating in the water (TilesetDefinition.waterScatter icebergs...) that the shore field should treat
// as land, so the water draws its edge fade / foam around them too. The builder registers discs per generating cube;
// WaterShoreBaker / TilesetPreviewScene stamp them into their land mask before the distance transform and re-finish
// when Version changes (icebergs can be built after the shore bake). View-only, not deterministic.
public static class ShoreObstacles
{
    private static readonly Dictionary<Object, List<Vector3>> byOwner = new();   // x, z = centre, y = radius (world)

    public static int Version { get; private set; }

    public static void Set(Object owner, List<Vector3> discs)
    {
        if (discs == null || discs.Count == 0)
        {
            Remove(owner);
            return;
        }
        byOwner[owner] = discs;
        Version++;
    }

    // Everything is being rebuilt (world switch): drop every disc, the rebuild registers the current ones again.
    public static void Clear()
    {
        if (byOwner.Count == 0)
            return;
        byOwner.Clear();
        Version++;
    }

    public static void Remove(Object owner)
    {
        if (owner != null && byOwner.Remove(owner))
            Version++;
    }

    // Marks every texel whose centre is inside a disc. Texel (x, y) centre = (x + 0.5 - res/2) * unitsPerTexel + centre.
    public static void Stamp(bool[] land, int res, float unitsPerTexel, Vector2 center)
    {
        // owners destroyed without unregistering (unloaded chunks) must not leave discs behind
        List<Object> dead = null;
        foreach (var owner in byOwner.Keys)
        {
            if (owner == null)
                (dead ??= new List<Object>()).Add(owner);
        }
        if (dead != null)
        {
            foreach (var o in dead)
                byOwner.Remove(o);
        }

        foreach (var list in byOwner.Values)
        foreach (var d in list)
        {
            var r = d.y;
            int x0 = Mathf.FloorToInt((d.x - r - center.x) / unitsPerTexel + res * 0.5f), x1 = Mathf.CeilToInt((d.x + r - center.x) / unitsPerTexel + res * 0.5f);
            int y0 = Mathf.FloorToInt((d.z - r - center.y) / unitsPerTexel + res * 0.5f), y1 = Mathf.CeilToInt((d.z + r - center.y) / unitsPerTexel + res * 0.5f);
            for (var y = Mathf.Max(y0, 0); y <= Mathf.Min(y1, res - 1); y++)
            for (var x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, res - 1); x++)
            {
                var wx = (x + 0.5f - res * 0.5f) * unitsPerTexel + center.x;
                var wz = (y + 0.5f - res * 0.5f) * unitsPerTexel + center.y;
                if ((wx - d.x) * (wx - d.x) + (wz - d.z) * (wz - d.z) <= r * r)
                    land[y * res + x] = true;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        byOwner.Clear();
        Version++;
    }
}
