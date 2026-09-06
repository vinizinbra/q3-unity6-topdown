using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Right-click (Project window) shortcut to flip one or more Texture2D assets horizontally in place,
/// overwriting the source image file on disk. Only .png/.jpg/.jpeg/.tga are re-encoded - anything else
/// (e.g. .psd) is skipped with a warning, since Unity's ImageConversion API can't re-author those.
/// </summary>
public static class FlipTextureHorizontallyContextMenu
{
    [MenuItem("Assets/Flip Texture Horizontally", false, 22)]
    private static void FlipSelected()
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Flip Texture Horizontally",
                $"This overwrites the source image file for {textures.Length} texture(s) on disk. " +
                "Undo only via version control. Continue?",
                "Flip", "Cancel"))
            return;

        int flipped = 0;
        int skipped = 0;

        foreach (var texture in textures)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (FlipTextureFile(path))
                flipped++;
            else
                skipped++;
        }

        AssetDatabase.Refresh();

        Debug.Log($"[FlipTextureHorizontallyContextMenu] Flipped {flipped} texture(s) horizontally" +
                   (skipped > 0 ? $", skipped {skipped} (unsupported format)." : "."));
    }

    [MenuItem("Assets/Flip Texture Horizontally", true)]
    private static bool ValidateFlipSelected() => Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

    private static bool FlipTextureFile(string assetPath)
    {
        Func<Texture2D, byte[]> encoder = ResolveEncoder(Path.GetExtension(assetPath));
        if (encoder == null)
        {
            Debug.LogWarning($"[FlipTextureHorizontallyContextMenu] Skipping '{assetPath}': unsupported " +
                              "format (only .png/.jpg/.jpeg/.tga can be re-encoded).");
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return false;

        bool wasReadable = importer.isReadable;
        if (!wasReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        Color32[] pixels = null;
        try
        {
            pixels = texture != null ? texture.GetPixels32() : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FlipTextureHorizontallyContextMenu] Skipping '{assetPath}': could not read pixels ({e.Message}).");
        }

        if (pixels == null)
        {
            RestoreReadable(importer, wasReadable);
            return false;
        }

        int width = texture.width;
        int height = texture.height;
        var flipped = new Color32[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
                flipped[rowStart + x] = pixels[rowStart + (width - 1 - x)];
        }

        var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        output.SetPixels32(flipped);
        output.Apply();
        byte[] bytes = encoder(output);
        UnityEngine.Object.DestroyImmediate(output);

        File.WriteAllBytes(assetPath, bytes);

        RestoreReadable(importer, wasReadable);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        return true;
    }

    private static Func<Texture2D, byte[]> ResolveEncoder(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".png": return t => t.EncodeToPNG();
            case ".jpg":
            case ".jpeg": return t => t.EncodeToJPG();
            case ".tga": return t => t.EncodeToTGA();
            default: return null;
        }
    }

    private static void RestoreReadable(TextureImporter importer, bool wasReadable)
    {
        if (importer.isReadable == wasReadable)
            return;

        importer.isReadable = wasReadable;
        importer.SaveAndReimport();
    }
}
