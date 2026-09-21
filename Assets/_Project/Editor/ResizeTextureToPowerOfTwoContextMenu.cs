using System;
using System.IO;
using Project.EditorTools.BuildAnalyzer;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Right-click (Project window) shortcut to resample one or more Texture2D assets so each axis lands on
/// its closest power of two, overwriting the source image file on disk. Scale only - no padding, so
/// the aspect ratio may change (e.g. 300x200 -> 256x256). Only .png/.jpg/.jpeg are rewritten; anything
/// else is skipped. Multiple-mode sprite sheets are skipped too, since their sprite rects would no
/// longer match the art - use the Build Size Analyzer's "Make POT" for those.
/// </summary>
public static class ResizeTextureToPowerOfTwoContextMenu
{
    private const string LogTag = "ResizeTextureToPowerOfTwo";

    [MenuItem("Assets/Resize Texture To Power Of 2", false, 23)]
    private static void ResizeSelected()
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Resize Texture To Power Of 2",
                $"This overwrites the source image file for {textures.Length} texture(s) on disk. " +
                "Undo only via version control. Continue?",
                "Resize", "Cancel"))
            return;

        int resized = 0;
        int unchanged = 0;
        int skipped = 0;

        foreach (var texture in textures)
        {
            switch (ResizeTextureFile(AssetDatabase.GetAssetPath(texture)))
            {
                case Result.Resized: resized++; break;
                case Result.Unchanged: unchanged++; break;
                default: skipped++; break;
            }
        }

        AssetDatabase.Refresh();

        LogHelper.Log(LogTag, $"Resized {resized} texture(s)" +
                  (unchanged > 0 ? $", {unchanged} already power of 2" : "") +
                  (skipped > 0 ? $", skipped {skipped} (see warnings)." : "."));
    }

    [MenuItem("Assets/Resize Texture To Power Of 2", true)]
    private static bool ValidateResizeSelected() => Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

    private enum Result { Resized, Unchanged, Skipped }

    private static Result ResizeTextureFile(string assetPath)
    {
        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
        {
            LogHelper.Warn(LogTag, $"Skipping '{assetPath}': unsupported format (only .png/.jpg/.jpeg can be re-encoded).");
            return Result.Skipped;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return Result.Skipped;

        if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Multiple)
        {
            LogHelper.Warn(LogTag, $"Skipping '{assetPath}': multi-sprite sheet, resizing would break its sprite rects. " +
                             "Use the Build Size Analyzer's \"Make POT\" instead.");
            return Result.Skipped;
        }

        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        Texture2D output = null;
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(assetPath), false))
            {
                LogHelper.Warn(LogTag, $"Skipping '{assetPath}': could not decode the source file.");
                return Result.Skipped;
            }

            int width = source.width, height = source.height;
            int newWidth = Mathf.ClosestPowerOfTwo(width), newHeight = Mathf.ClosestPowerOfTwo(height);
            if (newWidth == width && newHeight == height)
                return Result.Unchanged;

            Color32[] pixels = ImageResampler.Resample(source.GetPixels32(), width, height, newWidth, newHeight);
            output = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            output.SetPixels32(pixels);
            output.Apply(false, false);

            File.WriteAllBytes(assetPath, ext == ".png" ? output.EncodeToPNG() : output.EncodeToJPG(95));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            LogHelper.Log(LogTag, $"'{assetPath}': {width}x{height} -> {newWidth}x{newHeight}." +
                      (importer.textureType == TextureImporterType.Sprite
                          ? $" Sprite now renders at {(float)newWidth / width:P0} x {(float)newHeight / height:P0} of its previous size unless Pixels Per Unit ({importer.spritePixelsPerUnit}) is adjusted."
                          : ""));
            return Result.Resized;
        }
        catch (Exception e)
        {
            LogHelper.Warn(LogTag, $"Skipping '{assetPath}': {e.Message}");
            return Result.Skipped;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
        }
    }
}
