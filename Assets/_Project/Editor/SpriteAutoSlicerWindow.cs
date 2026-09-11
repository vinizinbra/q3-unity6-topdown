using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Right-click (Project window) sprite-sheet slicer. Detects icons purely by content - connected-component
/// labeling of the alpha channel (a small dilation bridges antialiasing seams within one icon, not gaps between
/// icons) - so it needs no column/row grid input and works on irregular sheets too. Each detected icon is
/// trimmed to its tight bounding box and, optionally, expanded to a centered square so every sprite comes out
/// uniform for UI use. Optionally reads "&lt;TextureName&gt;.names.txt" next to the source asset (one sprite
/// name per line, reading order top-to-bottom/left-to-right) instead of naming sprites "{prefix}_{index}".
/// </summary>
public class SpriteAutoSlicerWindow : EditorWindow
{
    private class DetectedSprite
    {
        public RectInt PixelRect;
        public string Name;
        public bool Include = true;
    }

    private Texture2D texture;
    private string texturePath;
    private int alphaThreshold = 16;
    private int bridgeGap = 1;
    private int minArea = 500;
    private bool fitToSquare = true;
    private int squarePaddingPercent = 0;
    private string namePrefix = "Sprite";
    private Color32[] pixelCache;
    private int texWidth, texHeight;
    private readonly List<DetectedSprite> detected = new();
    private Vector2 scroll;

    [MenuItem("Assets/SpriteUtil/Auto Slice...", false, 23)]
    private static void Open()
    {
        var tex = Selection.activeObject as Texture2D;
        if (tex == null)
            return;

        var window = GetWindow<SpriteAutoSlicerWindow>(true, "Auto-Slice & Trim Sprites");
        window.minSize = new Vector2(360, 480);
        window.Initialize(tex);
    }

    [MenuItem("Assets/SpriteUtil/Auto Slice...", true)]
    private static bool Validate() => Selection.activeObject is Texture2D;

    [MenuItem("Assets/SpriteUtil/Clear Slices", false, 24)]
    private static void ClearSlicedSprites()
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Clear Sliced Sprites",
                $"This removes all sliced sub-sprites from {textures.Length} texture(s) and resets each to a single " +
                "sprite. Anything referencing a named sub-sprite (e.g. RR_Coin) will lose that reference. Continue?",
                "Clear", "Cancel"))
            return;

        int cleared = 0, skipped = 0;
        foreach (var texture in textures)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                skipped++;
                continue;
            }

#pragma warning disable CS0618 // legacy spritesheet API - still the simplest way to script slicing
            importer.spritesheet = System.Array.Empty<SpriteMetaData>();
#pragma warning restore CS0618
            importer.spriteImportMode = SpriteImportMode.Single;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            cleared++;
        }

        Debug.Log($"[SpriteAutoSlicerWindow] Cleared sliced sprites on {cleared} texture(s)" +
                   (skipped > 0 ? $", skipped {skipped} (not in Multiple sprite mode)." : "."));
    }

    [MenuItem("Assets/SpriteUtil/Clear Slices", true)]
    private static bool ValidateClearSlicedSprites() => Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

    private void Initialize(Texture2D tex)
    {
        texture = tex;
        texturePath = AssetDatabase.GetAssetPath(tex);
        namePrefix = Path.GetFileNameWithoutExtension(texturePath);
        LoadPixels();
        Detect();
    }

    private void LoadPixels()
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
            return;

        bool wasReadable = importer.isReadable;
        if (!wasReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        var readableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        pixelCache = readableTexture.GetPixels32();
        texWidth = readableTexture.width;
        texHeight = readableTexture.height;

        if (!wasReadable)
        {
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }

    private void Detect()
    {
        detected.Clear();
        if (pixelCache == null)
            return;

        var mask = new bool[pixelCache.Length];
        for (int i = 0; i < pixelCache.Length; i++)
            mask[i] = pixelCache[i].a >= alphaThreshold;

        bool[] dilated = Dilate(mask, texWidth, texHeight, bridgeGap);

        var labels = new int[dilated.Length];
        var queue = new Queue<int>();
        int[] dxs = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dys = { -1, -1, -1, 0, 0, 1, 1, 1 };
        int labelCount = 0;

        for (int start = 0; start < dilated.Length; start++)
        {
            if (!dilated[start] || labels[start] != 0)
                continue;

            labelCount++;
            labels[start] = labelCount;
            queue.Clear();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % texWidth, cy = cur / texWidth;
                for (int n = 0; n < 8; n++)
                {
                    int nx = cx + dxs[n], ny = cy + dys[n];
                    if (nx < 0 || nx >= texWidth || ny < 0 || ny >= texHeight)
                        continue;

                    int nidx = ny * texWidth + nx;
                    if (!dilated[nidx] || labels[nidx] != 0)
                        continue;

                    labels[nidx] = labelCount;
                    queue.Enqueue(nidx);
                }
            }
        }

        var minX = new int[labelCount];
        var minY = new int[labelCount];
        var maxX = new int[labelCount];
        var maxY = new int[labelCount];
        var area = new int[labelCount];
        for (int i = 0; i < labelCount; i++)
        {
            minX[i] = int.MaxValue;
            minY[i] = int.MaxValue;
            maxX[i] = int.MinValue;
            maxY[i] = int.MinValue;
        }

        for (int idx = 0; idx < mask.Length; idx++)
        {
            if (!mask[idx])
                continue;

            int label = labels[idx];
            if (label == 0)
                continue;

            int li = label - 1;
            int x = idx % texWidth, y = idx / texWidth;
            if (x < minX[li]) minX[li] = x;
            if (x > maxX[li]) maxX[li] = x;
            if (y < minY[li]) minY[li] = y;
            if (y > maxY[li]) maxY[li] = y;
            area[li]++;
        }

        var rects = new List<RectInt>();
        for (int i = 0; i < labelCount; i++)
        {
            if (area[i] < minArea)
                continue;

            var rect = new RectInt(minX[i], minY[i], maxX[i] - minX[i] + 1, maxY[i] - minY[i] + 1);
            rects.Add(fitToSquare ? FitSquare(rect) : rect);
        }

        foreach (var rect in OrderReadingOrder(rects))
        {
            detected.Add(new DetectedSprite
            {
                PixelRect = rect,
                Name = $"{namePrefix}_{detected.Count + 1:00}",
            });
        }

        ApplyNamesFile();
    }

    private static bool[] Dilate(bool[] mask, int width, int height, int radius)
    {
        if (radius <= 0)
            return mask;

        var temp = new bool[mask.Length];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                bool found = false;
                int xs = Mathf.Max(0, x - radius), xe = Mathf.Min(width - 1, x + radius);
                for (int xx = xs; xx <= xe && !found; xx++)
                    found = mask[rowStart + xx];
                temp[rowStart + x] = found;
            }
        }

        var result = new bool[mask.Length];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool found = false;
                int ys = Mathf.Max(0, y - radius), ye = Mathf.Min(height - 1, y + radius);
                for (int yy = ys; yy <= ye && !found; yy++)
                    found = temp[yy * width + x];
                result[y * width + x] = found;
            }
        }

        return result;
    }

    private RectInt FitSquare(RectInt r)
    {
        float pad = Mathf.Max(0, squarePaddingPercent) / 100f;
        int side = Mathf.CeilToInt(Mathf.Max(r.width, r.height) * (1f + pad));
        side = Mathf.Min(side, Mathf.Min(texWidth, texHeight));

        float centerX = r.x + r.width / 2f;
        float centerY = r.y + r.height / 2f;
        int sqX = Mathf.Clamp(Mathf.RoundToInt(centerX - side / 2f), 0, texWidth - side);
        int sqY = Mathf.Clamp(Mathf.RoundToInt(centerY - side / 2f), 0, texHeight - side);

        return new RectInt(sqX, sqY, side, side);
    }

    private static List<RectInt> OrderReadingOrder(List<RectInt> rects)
    {
        if (rects.Count == 0)
            return rects;

        // GetPixels32 has y=0 at the BOTTOM of the texture, so "top of image first" means descending y.
        var withCenters = rects
            .Select(r => (rect: r, cx: r.x + r.width / 2f, cy: r.y + r.height / 2f))
            .OrderByDescending(t => t.cy)
            .ToList();

        float rowThreshold = (float)rects.Average(r => r.height) * 0.6f;
        var rowGroups = new List<List<(RectInt rect, float cx, float cy)>>();
        foreach (var item in withCenters)
        {
            var lastRow = rowGroups.Count > 0 ? rowGroups[^1] : null;
            if (lastRow != null && Mathf.Abs(item.cy - lastRow[0].cy) <= rowThreshold)
                lastRow.Add(item);
            else
                rowGroups.Add(new List<(RectInt, float, float)> { item });
        }

        var result = new List<RectInt>();
        foreach (var row in rowGroups)
            result.AddRange(row.OrderBy(t => t.cx).Select(t => t.rect));

        return result;
    }

    private void ApplyNamesFile()
    {
        string directory = Path.GetDirectoryName(texturePath) ?? "";
        string namesPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(texturePath) + ".names.txt");
        if (!File.Exists(namesPath))
            return;

        string[] lines = File.ReadAllLines(namesPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length != detected.Count)
        {
            Debug.LogWarning($"[SpriteAutoSlicerWindow] '{namesPath}' has {lines.Length} name(s) but " +
                              $"{detected.Count} sprite(s) were detected - ignoring the names file.");
            return;
        }

        for (int i = 0; i < detected.Count; i++)
            detected[i].Name = lines[i];
    }

    private void OnGUI()
    {
        if (texture == null)
        {
            Close();
            return;
        }

        EditorGUILayout.LabelField("Texture", texture.name);
        alphaThreshold = EditorGUILayout.IntSlider("Alpha Threshold", alphaThreshold, 1, 255);
        bridgeGap = EditorGUILayout.IntSlider(new GUIContent("Bridge Gap", "Bridges antialiasing seams within one icon. Keep small - large values can merge separate icons."), bridgeGap, 0, 4);
        minArea = EditorGUILayout.IntField(new GUIContent("Min Area (px)", "Discards specks smaller than this many opaque pixels."), minArea);
        fitToSquare = EditorGUILayout.Toggle("Fit To Square", fitToSquare);
        using (new EditorGUI.DisabledScope(!fitToSquare))
            squarePaddingPercent = EditorGUILayout.IntSlider(new GUIContent("Square Padding %", "Extra breathing room added around each icon before squaring. Some icons on this sheet sit only ~8-10px apart - keep this low or check the previews for bleed."), squarePaddingPercent, 0, 30);
        namePrefix = EditorGUILayout.TextField("Name Prefix", namePrefix);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Re-Detect"))
            Detect();
        if (GUILayout.Button("Reload Names File"))
            ApplyNamesFile();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField($"Detected {detected.Count} sprite(s).");

        scroll = EditorGUILayout.BeginScrollView(scroll);
        const float thumb = 48f;
        foreach (var d in detected)
        {
            EditorGUILayout.BeginHorizontal();
            d.Include = EditorGUILayout.Toggle(d.Include, GUILayout.Width(18));

            Rect thumbRect = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
            Rect uv = new Rect(
                (float)d.PixelRect.x / texWidth,
                (float)d.PixelRect.y / texHeight,
                (float)d.PixelRect.width / texWidth,
                (float)d.PixelRect.height / texHeight);
            GUI.DrawTextureWithTexCoords(thumbRect, texture, uv);

            d.Name = EditorGUILayout.TextField(d.Name);
            EditorGUILayout.LabelField($"{d.PixelRect.width}x{d.PixelRect.height}", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        int includeCount = detected.Count(d => d.Include);
        using (new EditorGUI.DisabledScope(includeCount == 0))
        {
            if (GUILayout.Button($"Apply Slice ({includeCount} sprite(s))", GUILayout.Height(30)))
                Apply();
        }
    }

    private void Apply()
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[SpriteAutoSlicerWindow] Could not find TextureImporter at {texturePath}");
            return;
        }

        List<DetectedSprite> included = detected.Where(d => d.Include).ToList();
        List<string> duplicateNames = included.GroupBy(d => d.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateNames.Count > 0)
        {
            EditorUtility.DisplayDialog("Duplicate Names",
                $"Duplicate sprite name(s): {string.Join(", ", duplicateNames)}. Make names unique before applying.",
                "OK");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        SpriteMetaData[] metadata = included.Select(d => new SpriteMetaData
        {
            name = d.Name,
            rect = new Rect(d.PixelRect.x, d.PixelRect.y, d.PixelRect.width, d.PixelRect.height),
            alignment = (int)SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
        }).ToArray();

#pragma warning disable CS0618 // legacy spritesheet API - still the simplest way to script slicing
        importer.spritesheet = metadata;
#pragma warning restore CS0618

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        Debug.Log($"[SpriteAutoSlicerWindow] Sliced {metadata.Length} trimmed sprite(s) from {texturePath}.");
        Close();
    }
}
