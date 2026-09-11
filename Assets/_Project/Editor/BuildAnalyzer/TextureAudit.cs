using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Per-texture checks over every texture the build actually shipped: NPOT, uncompressed, mipmaps on
    /// sprites, Read/Write, wasted alpha channel, oversized, shipped beside its atlas, and not atlased.
    /// Import settings are read for the platform the report was built for.
    /// </summary>
    internal static class TextureAudit
    {
        public const int AtlasFriendlyMaxDim = 1024;
        public const int LargeTextureDim = 2048;

        private static readonly HashSet<string> UncompressedFormats = new HashSet<string>
        {
            "RGBA32", "ARGB32", "RGB24", "RGBA16", "ARGB16", "RGB16", "Alpha8", "R8", "R16", "RG16",
            "RGBAHalf", "RGBAFloat", "RGB9E5", "RG32", "RGB48", "RGBA64",
        };

        private static readonly HashSet<string> AlphaFormatsWithRgbAlternative = new HashSet<string>
        {
            "DXT5", "DXT5Crunched", "BC7", "ETC2_RGBA8", "ETC2_RGBA8Crunched", "PVRTC_RGBA2", "PVRTC_RGBA4",
            "RGBA32", "ARGB32", "RGBA16", "ARGB16",
        };

        public static void Run(BuildAnalysis a, ProgressCallback progress)
        {
            List<AssetEntry> entries = a.Assets
                .Where(e => e.Category == AssetCategory.Texture && e.IsUnderAssets)
                .ToList();

            progress("Mapping material → texture references…", 0.24f);
            Dictionary<string, List<string>> materialsByTexture = CollectMaterialTextureReferences(a);

            for (int i = 0; i < entries.Count; i++)
            {
                AssetEntry entry = entries[i];
                if (i % 10 == 0)
                    progress($"Auditing textures ({i + 1}/{entries.Count})…", 0.25f + 0.3f * i / Math.Max(1, entries.Count));

                var importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
                if (importer == null) continue;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Path);
                if (texture == null) continue;

                TextureInfo info = Describe(a, entry, importer, texture);
                if (materialsByTexture.TryGetValue(entry.Path, out List<string> materials))
                    info.ReferencingMaterials.AddRange(materials);
                a.Textures.Add(info);
                a.TextureByPath[entry.Path] = info;
                Emit(a, info, importer);
            }

            a.Textures.Sort((x, y) => y.Entry.TotalPackedSize.CompareTo(x.Entry.TotalPackedSize));
        }

        /// <summary>
        /// Which materials in the build reference which textures. A material can only hold a Texture
        /// reference (never a Sprite), so this is the one direct-reference source that can be named with
        /// certainty — it is the usual reason an atlased sprite's source texture still ships.
        /// </summary>
        private static Dictionary<string, List<string>> CollectMaterialTextureReferences(BuildAnalysis a)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (AssetEntry material in a.Assets.Where(e => e.Category == AssetCategory.Material && e.IsUnderAssets))
            {
                string[] dependencies;
                try { dependencies = AssetDatabase.GetDependencies(material.Path, false); }
                catch { continue; }

                foreach (string dependency in dependencies)
                {
                    if (dependency == material.Path) continue;
                    if (!a.ByPath.TryGetValue(dependency, out AssetEntry dep) || dep.Category != AssetCategory.Texture) continue;

                    if (!result.TryGetValue(dependency, out List<string> list))
                    {
                        list = new List<string>();
                        result.Add(dependency, list);
                    }
                    list.Add(material.Path);
                }
            }

            return result;
        }

        private static TextureInfo Describe(BuildAnalysis a, AssetEntry entry, TextureImporter importer, Texture2D texture)
        {
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(a.TexturePlatform);
            bool overridden = platform != null && platform.overridden;
            if (!overridden)
                platform = importer.GetDefaultPlatformTextureSettings();

            TextureImporterFormat format = platform.format;
            if (format == TextureImporterFormat.Automatic)
            {
                try { format = importer.GetAutomaticFormat(a.TexturePlatform); }
                catch { /* keep Automatic */ }
            }

            long textureObjectSize = 0;
            foreach (KeyValuePair<string, long> kv in entry.SizeByType)
            {
                if (kv.Key == "Texture2D" || kv.Key == "Cubemap" || kv.Key == "Texture2DArray" || kv.Key == "Texture3D")
                    textureObjectSize += kv.Value;
            }

            int width = texture.width;
            int height = texture.height;
            double pixels = Math.Max(1.0, (double)width * height);
            double mipFactor = importer.mipmapEnabled ? 4.0 / 3.0 : 1.0;

            var info = new TextureInfo
            {
                Entry = entry,
                Path = entry.Path,
                Width = width,
                Height = height,
                Format = format.ToString(),
                TextureType = importer.textureType,
                Compression = platform.textureCompression,
                MaxSize = platform.maxTextureSize,
                Mipmaps = importer.mipmapEnabled,
                Readable = importer.isReadable,
                Npot = !Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height),
                PlatformOverridden = overridden,
                IsSprite = importer.textureType == TextureImporterType.Sprite,
                BytesPerPixel = textureObjectSize > 0 ? textureObjectSize / pixels / mipFactor : 0,
                TextureObjectSize = textureObjectSize,
            };

            try { info.HasAlpha = importer.DoesSourceTextureHaveAlpha(); }
            catch { info.HasAlpha = true; }

            if (a.AtlasMembership.TryGetValue(entry.Path, out List<AtlasReport> atlases))
                info.InAtlases.AddRange(atlases);

            return info;
        }

        private static void Emit(BuildAnalysis a, TextureInfo info, TextureImporter importer)
        {
            string path = info.Path;
            long size = info.Entry.TotalPackedSize;
            long texSize = info.TextureObjectSize;

            // Uncompressed (explicit setting, explicit format, or measured from what the build wrote)
            bool explicitlyUncompressed = info.Compression == TextureImporterCompression.Uncompressed
                                          || UncompressedFormats.Contains(info.Format);
            bool measuredUncompressed = texSize > 0 && info.BytesPerPixel >= 3.5;
            if (texSize > 0 && (explicitlyUncompressed || measuredUncompressed))
            {
                info.Uncompressed = true;
                long savings = texSize - texSize / 6;
                string why = explicitlyUncompressed
                    ? $"format {info.Format}, compression {info.Compression}"
                    : $"{info.BytesPerPixel:0.00} B/px measured in the build although the setting is {info.Compression} / {info.Format}" +
                      (info.Npot ? " — NPOT textures cannot use block compression on some formats" : "");

                Finding f = a.Add(size >= 2 * 1024 * 1024 ? Severity.Error : Severity.Warning,
                    FindingKind.Uncompressed, path,
                    $"Uncompressed in the build ({why}). {info.Width}×{info.Height}, {Fmt.Bytes(texSize)}.", savings);

                if (info.Compression == TextureImporterCompression.Uncompressed)
                {
                    f.AddFix("Set Compressed", () => SetCompressed(a, info),
                        $"Sets texture compression to Compressed / Automatic format for {a.TexturePlatform} and reimports.");
                }
            }

            // NPOT — irrelevant for a sprite that only ships through an atlas (the atlas page is POT).
            bool atlasedOnly = info.IsSprite && info.InAtlas && texSize == 0;
            if (info.Npot && importer.npotScale == TextureImporterNPOTScale.None && !atlasedOnly)
            {
                bool costly = texSize > 0 && info.BytesPerPixel >= 2.0;
                a.Add(costly ? Severity.Warning : Severity.Info, FindingKind.Npot, path,
                    $"Non-power-of-two {info.Width}×{info.Height}" +
                    (costly
                        ? $" and {info.BytesPerPixel:0.00} B/px in the build — compression is not applied."
                        : $" ({info.BytesPerPixel:0.00} B/px in the build — compression is fine; only relevant if you switch format or need mipmaps).") +
                    $" {MakePotDescription(info)}")
                    .AddFix("Make POT", () => MakePowerOfTwo(a, info), MakePotDescription(info));
            }

            // Mipmaps on sprites / UI
            if (info.Mipmaps && (info.TextureType == TextureImporterType.Sprite || info.TextureType == TextureImporterType.GUI))
            {
                a.Add(Severity.Warning, FindingKind.MipmapsOnSprite, path,
                    $"Mipmaps enabled on a {info.TextureType} texture — 33% extra size and blurrier UI.", texSize / 4)
                    .AddFix("Disable mipmaps", () => DisableMipmaps(a, info));
            }

            // Read/Write
            if (info.Readable)
            {
                a.Add(Severity.Info, FindingKind.ReadWrite, path,
                    "Read/Write enabled — a CPU copy is kept in memory at runtime (no build-size cost). Disable unless code reads its pixels.");
            }

            // Alpha channel wasted
            if (!info.HasAlpha && AlphaFormatsWithRgbAlternative.Contains(info.Format))
            {
                a.Add(Severity.Info, FindingKind.NoAlphaButAlphaFormat, path,
                    $"Source has no alpha but the build format is {info.Format}. An RGB format would halve it.", texSize / 2)
                    .AddFix("Alpha Source → None", () => SetAlphaSourceNone(a, info),
                        "Sets Alpha Source to None so Automatic picks an RGB format, then reimports.");
            }

            // Very large
            if (info.MaxDim > LargeTextureDim)
            {
                a.Add(Severity.Info, FindingKind.LargeTexture, path,
                    $"Very large texture {info.Width}×{info.Height} ({Fmt.Bytes(texSize)}). Check Max Size for {a.TexturePlatform}.");
            }

            // Packed in an atlas but the source texture is still written into the build
            if (info.InAtlas && texSize > 0)
            {
                info.SourceShippedBesideAtlas = true;
                string why = info.ReferencingMaterials.Count > 0
                    ? $"Referenced as a texture by material(s): {string.Join(", ", info.ReferencingMaterials.Select(System.IO.Path.GetFileName))}. " +
                      "If the material is only used on a SpriteRenderer/Image (the sprite supplies the texture at runtime), clear the " +
                      "material's texture slot; if it really needs the texture (particles, mesh), remove the texture from the atlas instead."
                    : "Something references the Texture2D directly (an Image.texture / RawImage, a ScriptableObject field of type " +
                      "Texture, a mesh material, or a sprite from this texture that the atlas does not cover).";

                Finding f = a.Add(Severity.Warning, FindingKind.SourceShippedBesideAtlas, path,
                    $"Packed in {info.AtlasNames} but the source texture is also written into the build ({Fmt.Bytes(texSize)}) — " +
                    $"its pixels ship twice. {why}", texSize);
                f.RelatedPaths = info.ReferencingMaterials.ToArray();
                if (info.ReferencingMaterials.Count > 0)
                    f.AddFix("Select material(s)", () => SelectAll(info.ReferencingMaterials), "Selects the referencing materials in the Project window.");
                foreach (AtlasReport atlas in info.InAtlases.ToList())
                {
                    AtlasReport atlasLocal = atlas;
                    f.AddFix($"Remove from {atlas.Name}", () => AtlasActions.RemoveFromAtlas(a, atlasLocal, path),
                        "Stops packing it (the texture then ships once, as a plain texture).");
                }
            }

            // Sprite that is not atlased
            if (info.IsSprite && !info.InAtlas && info.Entry.IsProjectAsset && info.MaxDim <= AtlasFriendlyMaxDim)
            {
                bool suggestUi = SuggestUiAtlas(path);
                Finding f = a.Add(Severity.Info, FindingKind.NotInAtlas, path,
                    $"Sprite texture {info.Width}×{info.Height} not in any atlas. Suggested: {(suggestUi ? "UI" : "Gameplay")} atlas.");

                if (suggestUi)
                {
                    f.AddFix("→ UI Atlas (suggested)", () => AtlasActions.AddToAtlas(a, info, AddTextureToAtlasContextMenu.UiAtlasPath));
                    f.AddFix("→ Game Atlas", () => AtlasActions.AddToAtlas(a, info, AddTextureToAtlasContextMenu.GameplayAtlasPath));
                }
                else
                {
                    f.AddFix("→ Game Atlas (suggested)", () => AtlasActions.AddToAtlas(a, info, AddTextureToAtlasContextMenu.GameplayAtlasPath));
                    f.AddFix("→ UI Atlas", () => AtlasActions.AddToAtlas(a, info, AddTextureToAtlasContextMenu.UiAtlasPath));
                }
            }
        }

        public static bool SuggestUiAtlas(string path)
        {
            return path.IndexOf("/UI/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/Icons/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("UpgradeIcons", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/GUI", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/HUD/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TextureImporter ImporterFor(string path) => AssetImporter.GetAtPath(path) as TextureImporter;

        private static void SelectAll(List<string> paths)
        {
            UnityEngine.Object[] objects = paths.Select(AssetDatabase.LoadMainAssetAtPath).Where(o => o != null).ToArray();
            Selection.objects = objects;
            if (objects.Length > 0) EditorGUIUtility.PingObject(objects[0]);
        }

        private static readonly HashSet<string> PaddableExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg" };

        /// <summary>What <see cref="MakePowerOfTwo"/> would do to this texture, for tooltips and messages.</summary>
        public static string MakePotDescription(TextureInfo info)
        {
            string ext = System.IO.Path.GetExtension(info.Path).ToLowerInvariant();
            if (!PaddableExtensions.Contains(ext))
            {
                return info.IsSprite
                    ? $"Make POT cannot rewrite a {ext} sprite source — export it as PNG first."
                    : $"Make POT sets the importer's Non-Power-of-2 scale to ToNearest ({ext} sources are not rewritten).";
            }

            int sw = Mathf.ClosestPowerOfTwo(Mathf.Max(1, info.Width)), sh = Mathf.ClosestPowerOfTwo(Mathf.Max(1, info.Height));
            int square = Mathf.Max(sw, sh);
            bool single = info.IsSprite && IsSingleSprite(info.Path);
            return $"Make POT rewrites the file: scales {info.Width}×{info.Height} → {sw}×{sh} (closest POT per axis), then pads " +
                   $"with transparent pixels to a {square}×{square} square" +
                   (info.IsSprite
                       ? single
                           ? " (single sprite: centered, so a Center pivot still lands on the art)."
                           : " (sprite sheet: anchored bottom-left, sprite rects are scaled to match)."
                       : " (anchored bottom-left).");
        }

        private static bool IsSingleSprite(string path) =>
            AssetImporter.GetAtPath(path) is TextureImporter importer && importer.spriteImportMode == SpriteImportMode.Single;

        /// <summary>
        /// Rewrites the source file: (1) resample each axis to its closest power of two, (2) pad with
        /// transparent pixels to a square. Sprite-sheet rects and borders are scaled by the same factors
        /// (bottom-left anchored, so the padding never touches them); single sprites are centered in the
        /// square. Non-image sources (PSD/TGA…) fall back to the importer's NPOT scale when not sprites.
        /// </summary>
        public static void MakePowerOfTwo(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            string ext = System.IO.Path.GetExtension(info.Path).ToLowerInvariant();
            if (!PaddableExtensions.Contains(ext))
            {
                if (info.IsSprite)
                {
                    LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': cannot rewrite a {ext} sprite source. Export it as PNG first.");
                    return;
                }

                importer.npotScale = TextureImporterNPOTScale.ToNearest;
                importer.SaveAndReimport();
                RefreshDims(info);
                a.Resolve(info.Path, FindingKind.Npot);
                LogHelper.Log(BuildAnalysis.LogTag, $"'{info.Name}': NPOT scale set to ToNearest → imports as {info.Width}×{info.Height}.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D result = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(info.Path);
                if (!source.LoadImage(bytes, false))
                {
                    LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': could not decode the source file.");
                    return;
                }

                int w = source.width, h = source.height;
                int sw = Mathf.ClosestPowerOfTwo(w), sh = Mathf.ClosestPowerOfTwo(h);
                int square = Mathf.Max(sw, sh);
                if (sw == w && sh == h && w == h)
                {
                    LogHelper.Log(BuildAnalysis.LogTag, $"'{info.Name}': source file is already a {w}×{h} POT square; the imported {info.Width}×{info.Height} comes from Max Size / platform settings.");
                    a.Resolve(info.Path, FindingKind.Npot);
                    return;
                }

                float scaleX = (float)sw / w, scaleY = (float)sh / h;
                bool centered = info.IsSprite && importer.spriteImportMode == SpriteImportMode.Single;
                int ox = centered ? (square - sw) / 2 : 0;
                int oy = centered ? (square - sh) / 2 : 0;

                // 1) scale to the closest POT per axis, 2) pad to a square.
                Color32[] scaled = ImageResampler.Resample(source.GetPixels32(), w, h, sw, sh);
                var dst = new Color32[square * square]; // transparent black
                for (int y = 0; y < sh; y++)
                    Array.Copy(scaled, y * sw, dst, (y + oy) * square + ox, sw);

                result = new Texture2D(square, square, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels32(dst);
                result.Apply(false, false);

                byte[] output = ext == ".png" ? result.EncodeToPNG() : result.EncodeToJPG(95);
                File.WriteAllBytes(info.Path, output);

                string rectNote = "";
                if (info.IsSprite && importer.spriteImportMode == SpriteImportMode.Multiple)
                    rectNote = ScaleSpriteRects(importer, scaleX, scaleY);

                AssetDatabase.ImportAsset(info.Path, ImportAssetOptions.ForceUpdate);
                RefreshDims(info);
                a.Resolve(info.Path, FindingKind.Npot);

                LogHelper.Log(BuildAnalysis.LogTag,
                    $"'{info.Name}': {w}×{h} → scaled {sw}×{sh} (×{scaleX:0.###}, ×{scaleY:0.###}) → padded to {square}×{square} " +
                    $"({(centered ? "centered" : "bottom-left anchored")}, {(ext == ".png" ? "transparent" : "black — JPG has no alpha")} padding).{rectNote} " +
                    "The file was rewritten; revert with git if unwanted." +
                    (info.IsSprite && (Mathf.Abs(scaleX - 1f) > 0.01f || Mathf.Abs(scaleY - 1f) > 0.01f)
                        ? $" Sprites now render at {scaleX:P0} × {scaleY:P0} of their previous size unless Pixels Per Unit ({importer.spritePixelsPerUnit}) is adjusted."
                        : ""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
            }
        }

        /// <summary>Scales every sprite rect/border of a Multiple-mode sheet so they keep pointing at the same art.</summary>
        private static string ScaleSpriteRects(TextureImporter importer, float scaleX, float scaleY)
        {
            try
            {
                var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
                factory.Init();
                UnityEditor.U2D.Sprites.ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                if (provider == null) return " (sprite rects NOT scaled: no data provider)";

                provider.InitSpriteEditorDataProvider();
                SpriteRect[] rects = provider.GetSpriteRects();
                foreach (SpriteRect sprite in rects)
                {
                    Rect r = sprite.rect;
                    sprite.rect = new Rect(
                        Mathf.Round(r.x * scaleX), Mathf.Round(r.y * scaleY),
                        Mathf.Max(1f, Mathf.Round(r.width * scaleX)), Mathf.Max(1f, Mathf.Round(r.height * scaleY)));
                    Vector4 b = sprite.border;
                    sprite.border = new Vector4(Mathf.Round(b.x * scaleX), Mathf.Round(b.y * scaleY), Mathf.Round(b.z * scaleX), Mathf.Round(b.w * scaleY));
                }

                provider.SetSpriteRects(rects);
                provider.Apply();
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                return $" {rects.Length} sprite rect(s) scaled.";
            }
            catch (Exception e)
            {
                LogHelper.Warn(BuildAnalysis.LogTag, $"Could not scale sprite rects of '{importer.assetPath}': {e.Message}. Fix them in the Sprite Editor.");
                return " (sprite rects NOT scaled — see warning)";
            }
        }

        private static void RefreshDims(TextureInfo info)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(info.Path);
            if (texture == null) return;
            info.Width = texture.width;
            info.Height = texture.height;
            info.Npot = !Mathf.IsPowerOfTwo(info.Width) || !Mathf.IsPowerOfTwo(info.Height);
        }

        private static void DisableMipmaps(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            info.Mipmaps = false;
            a.Resolve(info.Path, FindingKind.MipmapsOnSprite);
            LogHelper.Log(BuildAnalysis.LogTag, $"Disabled mipmaps on '{info.Path}'.");
        }

        private static void SetCompressed(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            importer.textureCompression = TextureImporterCompression.Compressed;
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(a.TexturePlatform);
            if (platform != null && platform.overridden)
            {
                platform.textureCompression = TextureImporterCompression.Compressed;
                platform.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(platform);
            }

            importer.SaveAndReimport();
            info.Compression = TextureImporterCompression.Compressed;
            info.Uncompressed = false;
            a.Resolve(info.Path, FindingKind.Uncompressed);
            LogHelper.Log(BuildAnalysis.LogTag, $"Set compression to Compressed on '{info.Path}'. Re-analyze after the next build to confirm the packed size.");
        }

        private static void SetAlphaSourceNone(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            importer.alphaSource = TextureImporterAlphaSource.None;
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(a.TexturePlatform);
            if (platform != null && platform.overridden && AlphaFormatsWithRgbAlternative.Contains(platform.format.ToString()))
            {
                platform.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(platform);
            }

            importer.SaveAndReimport();
            a.Resolve(info.Path, FindingKind.NoAlphaButAlphaFormat);
            LogHelper.Log(BuildAnalysis.LogTag, $"Set Alpha Source to None on '{info.Path}'.");
        }

        /// <summary>Rough bytes-per-pixel of an importer format, used to estimate atlas duplication cost.</summary>
        public static double BytesPerPixelFor(string formatName, TextureImporterCompression compression, string platform)
        {
            if (string.IsNullOrEmpty(formatName)) return 1.0;

            if (formatName == "Automatic")
            {
                if (compression == TextureImporterCompression.Uncompressed) return 4.0;
                return platform == "Android" || platform == "iPhone" ? 0.5 : 1.0;
            }

            if (formatName.StartsWith("ASTC_", StringComparison.Ordinal))
            {
                string[] dims = formatName.Substring(5).Split('x');
                if (dims.Length == 2 && int.TryParse(dims[0], out int bw) && int.TryParse(dims[1], out int bh) && bw > 0 && bh > 0)
                    return 16.0 / (bw * bh);
                return 1.0;
            }

            switch (formatName)
            {
                case "DXT1": case "DXT1Crunched": case "ETC_RGB4": case "ETC_RGB4Crunched": case "ETC2_RGB4":
                case "ETC2_RGB4_PUNCHTHROUGH_ALPHA": case "PVRTC_RGB4": case "PVRTC_RGBA4": case "BC4": case "EAC_R":
                    return 0.5;
                case "DXT5": case "DXT5Crunched": case "BC7": case "BC5": case "BC6H": case "ETC2_RGBA8":
                case "ETC2_RGBA8Crunched": case "EAC_RG": case "Alpha8": case "R8":
                    return 1.0;
                case "PVRTC_RGB2": case "PVRTC_RGBA2":
                    return 0.25;
                case "RGBA32": case "ARGB32": case "RGB9E5":
                    return 4.0;
                case "RGB24":
                    return 3.0;
                case "RGBA16": case "ARGB16": case "RGB16": case "R16": case "RG16":
                    return 2.0;
                case "RGBAHalf":
                    return 8.0;
                case "RGBAFloat":
                    return 16.0;
            }

            return 1.0;
        }
    }
}
