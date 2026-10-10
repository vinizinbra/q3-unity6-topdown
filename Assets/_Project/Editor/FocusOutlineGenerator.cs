using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Generates the "fake glow" sprites FocusOutlineWidget draws behind a focused UI element: for each listed
/// background sprite, a halo (hollow inside) that follows its exact shape (Euclidean distance, so chamfered corners stay
/// chamfered) - a thin crisp core hugging the edge plus a soft falloff out to <see cref="GlowRadius"/> px -
/// painted white (tinted at runtime). 9-slice borders grow by the same amount, so the glow fits the element
/// at any size. Sources are discovered from every FocusOutlineWidget in the open scene(s) and in the prefabs
/// under Assets/_Project/Prefabs. Results are registered in Resources/FocusOutlineSet and added to the
/// UISprites atlas. Re-run after adding a widget or changing the constants below / the source art.
/// </summary>
public static class FocusOutlineGenerator
{
    /// <summary>How far the glow reaches past the shape, in sprite px (1 sprite px = 1 canvas px at the project's UI PPU).</summary>
    public const int GlowRadius = 20;

    /// <summary>Width of the crisp, fully opaque ring hugging the edge. 0 = pure soft glow.</summary>
    public const float CoreThickness = 3f;

    /// <summary>Falloff curve of the halo: 1 = linear, 1.5 = default, 2 = softer/tighter, 3 = tight.</summary>
    public const float GlowPower = 1.5f;

    /// <summary>Peak alpha of the halo right next to the core (0-1).</summary>
    public const float GlowStrength = 1f;

    private const string OutputFolder = "Assets/_Project/Art/Sprites/UI/Focus";
    private const string SetPath = "Assets/_Project/Resources/FocusOutlineSet.asset";
    private const string AtlasPath = "Assets/_Project/Art/SpriteAtlases/UISprites.spriteatlas";

    // Every background sprite that can hold focus (see FocusOutlineWidget users).
    private static readonly string[] Sources =
    {
        "RR_MenuButton", "RR_MenuButtonBottom", "RR_WhiteSquare", "RR_Icon_Settings", "RR_Icon_Mail"
    };

    [MenuItem("Tools/RiftRaiders/UI/Generate Focus Outlines")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutputFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(SetPath));
        AssetDatabase.Refresh();

        var set = AssetDatabase.LoadAssetAtPath<FocusOutlineSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<FocusOutlineSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }

        // Existing entries stay (keyed by source sprite) and are rebuilt when their sprite is still in use.
        var entries = new Dictionary<Sprite, FocusOutlineSet.Entry>();
        foreach (FocusOutlineSet.Entry existing in set.entries)
        {
            if (existing.source != null)
                entries[existing.source] = existing;
        }

        HashSet<Sprite> sources = CollectSources();
        foreach (Sprite source in sources)
        {
            Sprite focus = Build(source);
            if (focus != null)
                entries[source] = new FocusOutlineSet.Entry { source = source, focus = focus, padding = GlowRadius };
        }

        var all = new List<FocusOutlineSet.Entry>(entries.Values);
        set.entries = all.ToArray();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[FocusOutline] Rebuilt {sources.Count} focus sprites ({all.Count} total, glow radius {GlowRadius}px) -> {OutputFolder}");
    }

    // Every background sprite of a FocusOutlineWidget in the open scene(s) or in a prefab under Assets/_Project/Prefabs -
    // add the widget to a new element and re-run, no list to maintain.
    private static HashSet<Sprite> CollectSources()
    {
        var sprites = new HashSet<Sprite>();

        foreach (FocusOutlineWidget widget in Resources.FindObjectsOfTypeAll<FocusOutlineWidget>())
        {
            if (widget.gameObject.scene.IsValid())
                AddBackground(widget, sprites);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null)
                continue;

            foreach (FocusOutlineWidget widget in prefab.GetComponentsInChildren<FocusOutlineWidget>(true))
                AddBackground(widget, sprites);
        }

        return sprites;
    }

    private static void AddBackground(FocusOutlineWidget widget, HashSet<Sprite> sprites)
    {
        var image = widget.ResolveBackground();
        if (image != null && image.sprite != null)
            sprites.Add(image.sprite);
    }

    private static Sprite Build(Sprite source)
    {
        // The imported texture is compressed and not readable - decode the PNG from disk instead.
        string sourcePath = AssetDatabase.GetAssetPath(source.texture);
        var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!full.LoadImage(File.ReadAllBytes(Path.GetFullPath(sourcePath))) || full.width != source.texture.width || full.height != source.texture.height)
        {
            Debug.LogError($"[FocusOutline] Could not read '{sourcePath}' at its imported size.");
            Object.DestroyImmediate(full);
            return null;
        }

        int w = (int)source.rect.width, h = (int)source.rect.height;
        int rx = (int)source.rect.x, ry = (int)source.rect.y;
        Color32[] fullPixels = full.GetPixels32();
        var src = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                src[y * w + x] = fullPixels[(ry + y) * full.width + rx + x].a;
        Object.DestroyImmediate(full);

        // The shape is everything the outside can't reach through transparent pixels, so a hollow
        // "outline only" sprite (transparent middle) still glows around its silhouette, not inside it.
        bool[] shape = FillShape(src, w, h);

        int n = GlowRadius, outW = w + 2 * n, outH = h + 2 * n;
        var output = new Color32[outW * outH];
        
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int sx = x - n, sy = y - n;
                float best = float.MaxValue;
                for (int dy = -n - 1; dy <= n + 1; dy++)
                {
                    int py = sy + dy;
                    if (py < 0 || py >= h) continue;
                    for (int dx = -n - 1; dx <= n + 1; dx++)
                    {
                        int px = sx + dx;
                        if (px < 0 || px >= w || !shape[py * w + px]) continue;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < best) best = d2;
                    }
                }

                // Hollow inside the source shape (so the glow can be drawn over neighbours without covering the
                // element itself); outside, a crisp core plus a soft halo. The shape's outermost pixel ring stays
                // opaque: bilinear filtering fades the hollow's edge over ~1px, and that fade must fall on the
                // element's own border - not outside it, where it showed the background as a thin light line.
                float distance = best == float.MaxValue ? n + 1f : Mathf.Sqrt(best);
                float core = Mathf.Clamp01(CoreThickness + 0.5f - distance);
                float glow = Mathf.Pow(Mathf.Clamp01(1f - distance / n), GlowPower) * GlowStrength;
                bool solid = sx >= 0 && sx < w && sy >= 0 && sy < h && shape[sy * w + sx];
                float alpha = solid ? (IsShapeEdge(shape, w, h, sx, sy) ? 1f : 0f) : Mathf.Max(core, glow);
                output[y * outW + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        var result = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        result.SetPixels32(output);
        result.Apply();
        string path = $"{OutputFolder}/{source.name}_Focus.png";
        File.WriteAllBytes(Path.GetFullPath(path), result.EncodeToPNG());
        Object.DestroyImmediate(result);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = source.pixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        Vector4 border = source.border;
        importer.spriteBorder = border == Vector4.zero ? Vector4.zero : border + new Vector4(n, n, n, n);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        AddToAtlas(path);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // A shape pixel with a non-shape (or out-of-rect) 8-neighbour.
    private static bool IsShapeEdge(bool[] shape, int w, int h, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !shape[ny * w + nx])
                    return true;
            }
        }

        return false;
    }

    private static bool[] FillShape(byte[] alpha, int w, int h)
    {
        // 1px transparent margin so the flood can wrap around a shape that touches the sprite rect's edge.
        int fw = w + 2, fh = h + 2;
        var outside = new bool[fw * fh];
        var queue = new Queue<int>();
        outside[0] = true;
        queue.Enqueue(0);

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % fw, y = index / fw;
            for (int i = 0; i < 4; i++)
            {
                int nx = x + (i == 0 ? 1 : i == 1 ? -1 : 0), ny = y + (i == 2 ? 1 : i == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= fw || ny >= fh)
                    continue;

                int next = ny * fw + nx;
                bool transparent = nx == 0 || ny == 0 || nx == fw - 1 || ny == fh - 1 || alpha[(ny - 1) * w + (nx - 1)] < 128;
                if (outside[next] || !transparent)
                    continue;

                outside[next] = true;
                queue.Enqueue(next);
            }
        }

        var shape = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                shape[y * w + x] = !outside[(y + 1) * fw + (x + 1)];
        return shape;
    }

    private static void AddToAtlas(string texturePath)
    {
        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (atlas == null || texture == null)
            return;

        foreach (Object packable in SpriteAtlasExtensions.GetPackables(atlas))
        {
            if (packable == texture)
                return;
        }

        SpriteAtlasExtensions.Add(atlas, new Object[] { texture });
        EditorUtility.SetDirty(atlas);
    }
}
