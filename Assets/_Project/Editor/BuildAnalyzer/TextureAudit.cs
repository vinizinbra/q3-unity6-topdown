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
    /// One size plan per texture the build shipped. The decision tree, in order:
    /// <list type="number">
    /// <item>Only its Sprites shipped (pixels came from an atlas) → nothing to do.</item>
    /// <item>Packed in an atlas but the Texture2D still shipped → <see cref="FindingKind.PendingRebuild"/> when the
    /// texture/atlas was edited after the build, else <see cref="FindingKind.DirectTextureReference"/> (who references
    /// the texture itself), then the standalone checks (it keeps shipping as a texture).</item>
    /// <item>A sprite that fits an atlas (UI or Gameplay by usage) → <see cref="FindingKind.AtlasCandidate"/>. The
    /// atlas page is already POT and compressed, so POT/×4 on the source would be pointless.</item>
    /// <item>Everything else ships as a plain texture → standalone checks: not a multiple of 4 (block compression
    /// impossible: <b>Pad to ×4</b> for sprites, <b>NPOT → ToNearest</b> for material textures), uncompressed,
    /// mipmaps on sprites, wasted alpha, and the quality tradeoffs Crunch / lower Max Size.</item>
    /// </list>
    /// Import settings are read for the platform the report was built for.
    /// </summary>
    internal static class TextureAudit
    {
        public const int LargeTextureDim = 2048;
        private const long CrunchMinSize = 256 * 1024;
        private const long OversizedSpriteMinSize = 512 * 1024;
        /// <summary>A sprite fits an atlas when its largest rect is ≤ page/4 …</summary>
        private const int AtlasDimDivisor = 4;
        /// <summary>… and all its sprites cover ≤ page²/8.</summary>
        private const int AtlasAreaDivisor = 8;

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

        private static readonly HashSet<string> RewritableExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg" };

        private static readonly string[] UiPathMarkers =
            { "/UI/", "Widget", "Window", "Canvas", "/HUD", "Menu", "Popup", "/Icons/", "UpgradeIcons", "/GUI", "Splash", "Loading" };

        private static readonly string[] GameplayPathMarkers =
            { "/Entities/", "/Enemy/", "/Enemies/", "Projectile", "/Weapons/", "LevelChunk", "/Props/", "/Characters/", "/Character/", "/Environment/", "/Guns/" };

        public static void Run(BuildAnalysis a, ProgressCallback progress)
        {
            List<AssetEntry> entries = a.Assets
                .Where(e => e.Category == AssetCategory.Texture && e.IsUnderAssets)
                .ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                AssetEntry entry = entries[i];
                if (i % 10 == 0)
                    progress($"Planning textures ({i + 1}/{entries.Count})…", 0.3f + 0.3f * i / Math.Max(1, entries.Count));

                var importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
                if (importer == null) continue;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Path);
                if (texture == null) continue;

                TextureInfo info = Describe(a, entry, importer, texture);
                a.Textures.Add(info);
                a.TextureByPath[entry.Path] = info;
                Plan(a, info, importer);
            }

            a.Textures.Sort((x, y) => y.Entry.TotalPackedSize.CompareTo(x.Entry.TotalPackedSize));
        }

        // ------------------------------------------------------------------ describe

        private static TextureInfo Describe(BuildAnalysis a, AssetEntry entry, TextureImporter importer, Texture2D texture)
        {
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(a.TexturePlatform);
            bool overridden = platform != null && platform.overridden;
            if (!overridden)
                platform = importer.GetDefaultPlatformTextureSettings();

            TextureImporterFormat format = platform.format;
            bool automatic = format == TextureImporterFormat.Automatic;
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
                SourceWidth = width,
                FormatIsAutomatic = automatic,
                SourceHeight = height,
                Format = format.ToString(),
                TextureType = importer.textureType,
                Compression = platform.textureCompression,
                MaxSize = platform.maxTextureSize,
                Mipmaps = importer.mipmapEnabled,
                Readable = importer.isReadable,
                Npot = !Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height),
                PlatformOverridden = overridden,
                IsSprite = importer.textureType == TextureImporterType.Sprite,
                Crunched = platform.crunchedCompression || format.ToString().EndsWith("Crunched", StringComparison.Ordinal),
                BytesPerPixel = textureObjectSize > 0 ? textureObjectSize / pixels / mipFactor : 0,
                TextureObjectSize = textureObjectSize,
            };

            try
            {
                importer.GetSourceTextureWidthAndHeight(out int sw, out int sh);
                info.SourceWidth = sw;
                info.SourceHeight = sh;
            }
            catch { /* keep imported dims */ }

            try { info.HasAlpha = importer.DoesSourceTextureHaveAlpha() || importer.alphaSource == TextureImporterAlphaSource.FromGrayScale; } // grayscale-derived alpha counts: the alpha channel is real, just not in the file
            catch { info.HasAlpha = true; }

            foreach (Sprite sprite in AssetDatabase.LoadAllAssetRepresentationsAtPath(entry.Path).OfType<Sprite>())
            {
                Rect r = sprite.rect;
                info.SpriteCount++;
                info.SpriteArea += (long)(r.width * r.height);
                info.LargestSpriteDim = Math.Max(info.LargestSpriteDim, (int)Math.Max(r.width, r.height));
            }

            if (info.SpriteCount == 0)
            {
                info.SpriteArea = (long)width * height;
                info.LargestSpriteDim = Math.Max(width, height);
            }

            if (a.AtlasMembership.TryGetValue(entry.Path, out List<AtlasReport> atlases))
                info.InAtlases.AddRange(atlases);

            if (a.References != null)
            {
                info.TextureReferencers.AddRange(TextureReferenceIndex.Of(a.References.TextureRefs, entry.Path));
                info.SpriteReferencers.AddRange(TextureReferenceIndex.Of(a.References.SpriteRefs, entry.Path));
            }

            bool tex = info.TextureReferencers.Count > 0, spr = info.SpriteReferencers.Count > 0;
            info.Use = tex && spr ? TextureUse.SpriteAndTexture : tex ? TextureUse.Texture : spr ? TextureUse.Sprite : TextureUse.Unknown;

            ResolveContext(a, info);
            info.ChangedSinceBuild = a.ChangedSinceBuild(entry.Path) || info.InAtlases.Any(r => r.ChangedSinceBuild);
            return info;
        }

        /// <summary>UI vs Gameplay: Sprite Optimizer usage scan if one was run, else who references it, else its path.</summary>
        private static void ResolveContext(BuildAnalysis a, TextureInfo info)
        {
            if (BuildAnalysis.ScannedContexts.TryGetValue(info.Path, out UsageContext scanned) && scanned != UsageContext.Unknown)
            {
                info.Context = scanned;
                info.ContextSource = "usage scan";
                return;
            }

            // Votes from the references themselves (component type, then field name).
            int ui = a.References != null && a.References.UiVotes.TryGetValue(info.Path, out int u) ? u : 0;
            int gameplay = a.References != null && a.References.GameplayVotes.TryGetValue(info.Path, out int g) ? g : 0;
            if (ui > 0 || gameplay > 0)
            {
                info.Context = ui > 0 && gameplay > 0 ? UsageContext.Both : ui > 0 ? UsageContext.UI : UsageContext.Gameplay;
                info.ContextSource = $"from its references ({ui} UI, {gameplay} gameplay)";
                return;
            }

            // Then who references it, by path.
            int uiPaths = 0, gameplayPaths = 0;
            foreach (string referencer in info.SpriteReferencers)
            {
                bool isUi = HasMarker(referencer, UiPathMarkers), isGameplay = HasMarker(referencer, GameplayPathMarkers);
                if (isUi && !isGameplay) uiPaths++;
                else if (isGameplay && !isUi) gameplayPaths++;
            }

            if (SuggestUiAtlas(info.Path) && gameplayPaths == 0)
            {
                info.Context = UsageContext.UI;
                info.ContextSource = "guessed from path";
                return;
            }

            if (uiPaths > 0 && gameplayPaths == 0)
            {
                info.Context = UsageContext.UI;
                info.ContextSource = "referenced by UI assets";
                return;
            }

            if (gameplayPaths > 0 && uiPaths == 0)
            {
                info.Context = UsageContext.Gameplay;
                info.ContextSource = "referenced by gameplay assets";
                return;
            }

            if (SuggestUiAtlas(info.Path))
            {
                info.Context = UsageContext.UI;
                info.ContextSource = "guessed from path";
            }
            else if (HasMarker(info.Path, GameplayPathMarkers))
            {
                info.Context = UsageContext.Gameplay;
                info.ContextSource = "guessed from path";
            }
            else
            {
                info.Context = UsageContext.Unknown;
                info.ContextSource = "unknown — run Scan sprite usage";
            }
        }

        private static bool HasMarker(string path, string[] markers)
        {
            foreach (string marker in markers)
                if (path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool SuggestUiAtlas(string path) => HasMarker(path, UiPathMarkers);

        // ------------------------------------------------------------------ plan

        private static void Plan(BuildAnalysis a, TextureInfo info, TextureImporter importer)
        {
            if (!info.ShipsAsTexture)
                return; // only its Sprites shipped — the pixels came from an atlas

            if (info.InAtlas)
            {
                if (info.ChangedSinceBuild)
                {
                    PendingRebuild(a, info);
                    return;
                }

                DirectReference(a, info);
                Standalone(a, info, importer, "it is packed but also referenced as a Texture2D");
                PickPlan(a, info);
                return;
            }

            string whyNotAtlas = AtlasFit(a, info, out AtlasReport target, out AtlasReport other);
            if (whyNotAtlas == null)
                AtlasCandidate(a, info, target, other);
            else
                Standalone(a, info, importer, whyNotAtlas);

            PickPlan(a, info);
        }

        private static void PickPlan(BuildAnalysis a, TextureInfo info)
        {
            List<Finding> own = a.Findings.Where(f => f.AssetPath == info.Path && f.PotentialSavings > 0).ToList();
            info.Plan = own.Where(f => !f.IsTradeoff).OrderByDescending(f => f.PotentialSavings).FirstOrDefault()
                        ?? own.OrderByDescending(f => f.PotentialSavings).FirstOrDefault();
        }

        /// <summary>Null when the texture should go into <paramref name="target"/>; otherwise why it shouldn't.</summary>
        private static string AtlasFit(BuildAnalysis a, TextureInfo info, out AtlasReport target, out AtlasReport other)
        {
            AtlasReport ui = a.Atlases.FirstOrDefault(r => r.IsUiAtlas);
            AtlasReport gameplay = a.Atlases.FirstOrDefault(r => r.IsGameplayAtlas);
            bool wantsUi = info.Context == UsageContext.UI
                           || (info.Context != UsageContext.Gameplay && SuggestUiAtlas(info.Path));
            target = wantsUi ? ui : gameplay;
            other = wantsUi ? gameplay : ui;

            if (!info.IsSprite)
                return $"its Texture Type is {info.TextureType} (atlases only pack Sprites) — it's a material/texture asset";
            if (info.Use == TextureUse.Texture)
                return $"it's only used as a Texture2D (by {Names(info.TextureReferencers)}) — an atlas can't serve materials/RawImages";
            if (info.Use == TextureUse.SpriteAndTexture)
                return $"it's also used as a Texture2D (by {Names(info.TextureReferencers)}), so packing it would ship its pixels twice";
            if (!info.Entry.IsUnderAssets || info.Entry.IsSuspiciousSource)
                return "it's outside the project art folders (package / demo / reference folder)";
            if (target == null)
                return $"there's no {(wantsUi ? "UI" : "Gameplay")} atlas at {(wantsUi ? AddTextureToAtlasContextMenu.UiAtlasPath : AddTextureToAtlasContextMenu.GameplayAtlasPath)}";

            int page = target.MaxPageSize;
            if (info.LargestSpriteDim > page / AtlasDimDivisor)
                return $"its largest sprite is {info.LargestSpriteDim}px, more than ¼ of a {page}px {target.Name} page — it would eat a page and break batching";
            if (info.SpriteArea > (long)page * page / AtlasAreaDivisor)
                return $"its sprites cover {info.SpriteArea:N0} px, more than ⅛ of a {page}² {target.Name} page";

            return null;
        }

        private static void AtlasCandidate(BuildAnalysis a, TextureInfo info, AtlasReport target, AtlasReport other)
        {
            long tex = info.TextureObjectSize;
            double cost = target.CostPerPixel;
            long atlasCost = (long)(info.SpriteArea * cost);
            long savings = tex - atlasCost;
            string context = info.Context == UsageContext.Unknown ? "context unknown" : $"{info.Context}, {info.ContextSource}";

            string message =
                $"{info.Width}×{info.Height} sprite texture ({info.SpriteCount} sprite(s), {info.SpriteArea:N0} px) ships on its own as " +
                $"{info.Format} at {info.BytesPerPixel:0.00} B/px = {Fmt.Bytes(tex)}. Packed in {target.Name} its sprites cost ≈{Fmt.Bytes(atlasCost)} " +
                $"({cost:0.###} B/px, measured from this build's {target.Name} pages) and batch with the other sprites there. " +
                "No POT / ×4 needed: the atlas page already is one.";
            if (info.Context == UsageContext.Both)
                message += " ⚠ The usage scan found it in BOTH UI and gameplay — one side will sample the other atlas; " +
                           "Sprite Optimizer ▸ Conflicts ▸ Duplicate splits it.";
            if (info.Use == TextureUse.Unknown)
                message += " No scene/prefab/asset references it in YAML, so it's probably loaded from code (Resources.Load / Addressables) — " +
                           "sprites loaded that way are served from the atlas too.";

            Finding f = a.Add(BuildAnalysis.SeverityFor(savings), FindingKind.AtlasCandidate, info.Path,
                $"Sprite in no atlas → {target.Name} ({context})", message, savings);
            f.RelatedPaths = info.SpriteReferencers.ToArray();
            f.AddFix($"→ {target.Name}", () => AtlasActions.AddToAtlas(a, info, target.Path),
                $"Adds the texture to {target.Path}. Its sprites are then drawn from the atlas page.");
            if (other != null)
                f.AddFix($"→ {other.Name}", () => AtlasActions.AddToAtlas(a, info, other.Path), $"Adds the texture to {other.Path} instead.");
        }

        private static void PendingRebuild(BuildAnalysis a, TextureInfo info)
        {
            Finding f = a.Add(Severity.Info, FindingKind.PendingRebuild, info.Path,
                $"Now in {info.AtlasNames} — rebuild to confirm",
                $"This build still wrote the {Fmt.Bytes(info.TextureObjectSize)} Texture2D, but the texture or {info.AtlasNames} was edited " +
                "after the build (most likely it was added to the atlas since). Make a new build and re-analyze: if it still ships " +
                "as a texture, something references the Texture2D directly.",
                info.TextureObjectSize);
            f.RelatedPaths = info.InAtlases.Select(r => r.Path).ToArray();
        }

        /// <summary>
        /// An atlased texture whose Texture2D still ships. Rule: atlas batching is never traded for size — a texture
        /// whose sprites are used is never offered "Remove from atlas". Only a texture with no sprite usage at all
        /// (nothing to batch) gets that option.
        /// </summary>
        private static void DirectReference(BuildAnalysis a, TextureInfo info)
        {
            long tex = info.TextureObjectSize;
            long atlasCopy = (long)info.InAtlases.Sum(r => info.SpriteArea * r.CostPerPixel);
            List<string> refs = info.TextureReferencers;
            string who = refs.Count > 0
                ? $"referenced as a Texture2D by {Names(refs)}"
                : "loaded as a Texture2D by something with no YAML reference (code: Resources.Load<Texture2D>, a binary asset, or a TMP sprite asset)";
            bool spritesUsed = info.Use == TextureUse.Sprite || info.Use == TextureUse.SpriteAndTexture
                               || BuildAnalysis.ScannedContexts.TryGetValue(info.Path, out UsageContext scanned) && scanned != UsageContext.Unknown;

            Finding f;
            if (spritesUsed && refs.Count > 0)
            {
                // Sprites batch from the atlas, materials need a real texture: shipping both is the price of batching.
                f = a.Add(Severity.Info, FindingKind.DirectTextureReference, info.Path,
                    $"Atlas sprite + material texture — give the material(s) their own copy",
                    $"Its sprites batch from {info.AtlasNames} (≈{Fmt.Bytes(atlasCopy)} there) and it is also {who}, so the {Fmt.Bytes(tex)} " +
                    "texture ships as well. That double cost is what keeping atlas batching costs — a material can't sample the atlas page. " +
                    "Copy for material(s) makes it explicit: the materials get their own Default-type copy you can shrink on its own " +
                    "(Max Size, Crunch — a particle rarely needs full resolution) without touching the atlas sprites.",
                    0);
                f.AddFix("Copy for material(s)", () => CopyForTextureUsers(a, info),
                    $"Duplicates the texture as a Default-type '…_Tex' copy and repoints {Names(refs)} to it. Sprites stay in {info.AtlasNames}.");
                f.AddFix("Select referencers", () => SelectAll(refs), "Selects the assets that reference the Texture2D.", resolves: false);
            }
            else if (spritesUsed)
            {
                f = a.Add(BuildAnalysis.SeverityFor(tex), FindingKind.DirectTextureReference, info.Path,
                    $"Packed in {info.AtlasNames} but the texture still ships",
                    $"Its sprites batch from {info.AtlasNames}, but the {Fmt.Bytes(tex)} Texture2D was written into the build too: it is {who}. " +
                    "Find what loads the texture itself (\"Why is this in the build?\" shows the chain) and switch it to the sprite. " +
                    "Keep it in the atlas — its sprites are in use.",
                    tex);
                f.AddFix("Select referencers", () => SelectAll(refs), "Selects the assets that reference the Texture2D.", resolves: false);
            }
            else
            {
                f = a.Add(BuildAnalysis.SeverityFor(atlasCopy), FindingKind.DirectTextureReference, info.Path,
                    $"No sprite of it is used — its {info.AtlasNames} copy is wasted",
                    $"It is {who} and no shipped asset uses its sprites, so the {Fmt.Bytes(tex)} texture has to ship anyway and the " +
                    $"≈{Fmt.Bytes(atlasCopy)} it takes in {info.AtlasNames} batches nothing. Remove it from the atlas." +
                    (BuildAnalysis.ScannedContexts.Count == 0 ? " (If code loads its sprites, run Scan sprite usage first — a used sprite keeps it in the atlas.)" : ""),
                    atlasCopy);
                foreach (AtlasReport atlas in info.InAtlases.ToList())
                {
                    AtlasReport atlasLocal = atlas;
                    f.AddFix($"Remove from {atlas.Name}", () => AtlasActions.RemoveFromAtlas(a, atlasLocal, info.Path),
                        "Stops packing it — the texture then ships once, as a plain texture. No sprite of it is used, so no batching is lost.");
                }
            }

            f.RelatedPaths = refs.ToArray();
        }

        /// <summary>
        /// Keeps the texture's sprites in the atlas and gives everything that uses the Texture2D itself (materials,
        /// RawImages, Texture fields, TMP sprite assets) its own Default-type copy next to the original
        /// ("Shadow_Tex.png"). Same importer settings otherwise, so it looks identical. References in scenes are listed,
        /// not edited (open the scene and repoint them by hand).
        /// </summary>
        public static void CopyForTextureUsers(BuildAnalysis a, TextureInfo info)
        {
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(info.Path);
            if (original == null) return;

            string dir = Path.GetDirectoryName(info.Path)?.Replace('\\', '/');
            string copyPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/{Path.GetFileNameWithoutExtension(info.Path)}_Tex{Path.GetExtension(info.Path)}");
            if (!AssetDatabase.CopyAsset(info.Path, copyPath))
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"Could not copy '{info.Path}' to '{copyPath}'.");
                return;
            }

            TextureImporter importer = ImporterFor(copyPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }

            var copy = AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath);
            int swapped = 0;
            var scenes = new List<string>();
            foreach (string referencer in info.TextureReferencers)
            {
                if (referencer.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    scenes.Add(referencer);
                    continue;
                }

                foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(referencer))
                {
                    if (obj == null) continue;
                    var so = new SerializedObject(obj);
                    SerializedProperty it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue != original) continue;
                        it.objectReferenceValue = copy;
                        changed = true;
                        swapped++;
                    }

                    if (!changed) continue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(obj);
                }
            }

            AssetDatabase.SaveAssets();
            a.Resolve(info.Path, FindingKind.DirectTextureReference);
            LogHelper.Log(BuildAnalysis.LogTag,
                $"'{info.Name}': created '{copyPath}' (Default type) and repointed {swapped} texture reference(s). Its sprites stay in {info.AtlasNames}." +
                (scenes.Count > 0 ? $" Repoint these scene references by hand: {string.Join(", ", scenes)}." : ""));
        }

        /// <summary>Checks for a texture that ships as a plain texture.</summary>
        private static void Standalone(BuildAnalysis a, TextureInfo info, TextureImporter importer, string whyNotAtlas)
        {
            string path = info.Path;
            long tex = info.TextureObjectSize;
            double mip = info.Mipmaps ? 4.0 / 3.0 : 1.0;
            double target = TargetBytesPerPixel(a, info);
            // An uncompressed format Automatic picked on its own (R8 for a single-channel texture on WebGL) is not a setting
            // "Set Compressed" can change — only an explicit Uncompressed setting or an explicit format counts.
            bool explicitUncompressed = info.Compression == TextureImporterCompression.Uncompressed
                                        || (UncompressedFormats.Contains(info.Format) && !info.FormatIsAutomatic && info.BytesPerPixel >= 2.0);
            bool fellBack = info.BytesPerPixel >= 2.0;
            bool notBlockCompressible = !info.MultipleOf4 && (fellBack || explicitUncompressed);
            string notAtlas = whyNotAtlas != null ? $" Not an atlas candidate: {whyNotAtlas}." : "";
            bool canNpotScale = !info.IsSprite;

            if (notBlockCompressible)
            {
                int padW = Ceil4(info.Width), padH = Ceil4(info.Height);
                long padSaved = tex - (long)(padW * (double)padH * target * mip);
                int potW = Mathf.ClosestPowerOfTwo(info.Width), potH = Mathf.ClosestPowerOfTwo(info.Height);
                long potSaved = tex - (long)(potW * (double)potH * target * mip);
                long savings = canNpotScale ? potSaved : padSaved;

                string head = $"{info.Width}×{info.Height} isn't a multiple of 4, so {a.TexturePlatform} can't block-compress it " +
                              $"(DXT/ETC2/ASTC work on 4×4 blocks) — it ships as {info.Format} at {info.BytesPerPixel:0.0} B/px: {Fmt.Bytes(tex)}.";
                string fix = canNpotScale
                    ? $" Non-Power-of-2 → ToNearest makes the importer rescale it to {potW}×{potH} (importer setting only; UVs stay 0–1 so " +
                      $"materials look the same) and it compresses to ≈{Fmt.Bytes(tex - potSaved)}."
                    : $" Pad to ×4 adds {padW - info.Width}×{padH - info.Height} transparent px (no rescale: sprite rects, pivots and borders are " +
                      $"kept) and it compresses to ≈{Fmt.Bytes(tex - padSaved)}.";

                Finding f = a.Add(BuildAnalysis.SeverityFor(savings), FindingKind.NotBlockCompressible, path,
                    canNpotScale ? $"NPOT {info.Width}×{info.Height} ships uncompressed → POT {potW}×{potH}" : $"{info.Width}×{info.Height} not ×4 → ships uncompressed; pad to {padW}×{padH}",
                    head + fix + notAtlas, savings);

                if (canNpotScale)
                {
                    f.AddFix("POT (NPOT → ToNearest)", () => SetNpotToNearest(a, info), $"Importer Non-Power-of-2 → ToNearest ({potW}×{potH}). Non-destructive.");
                    if (IsRewritable(path)) f.AddFix("Pad to ×4", () => PadToMultipleOf4(a, info), $"Rewrites the file to {padW}×{padH} with transparent padding.");
                }
                else
                {
                    f.AddFix("Pad to ×4", () => PadToMultipleOf4(a, info),
                        IsRewritable(path)
                            ? $"Rewrites the file to {padW}×{padH} with transparent padding (bottom-left anchored for sheets, pivot kept for single sprites). Revert with git."
                            : "Only PNG/JPG sources can be padded — re-export it as PNG.");
                    if (IsRewritable(path))
                        f.AddFix("Make POT (keeps slices)", () => MakePowerOfTwoKeepingSlices(a, info), MakePotDescription(info));
                }
            }
            else if (explicitUncompressed || (info.BytesPerPixel >= 3.5 && !info.FormatIsAutomatic))
            {
                long savings = tex - (long)((double)info.Width * info.Height * target * mip);
                Finding f = a.Add(BuildAnalysis.SeverityFor(savings), FindingKind.Uncompressed, path,
                    $"Uncompressed ({info.Format}) — compress",
                    $"Ships as {info.Format} ({info.Compression}) at {info.BytesPerPixel:0.0} B/px: {Fmt.Bytes(tex)}. Compressed it would be " +
                    $"≈{Fmt.Bytes(tex - savings)}.{notAtlas}", savings);
                f.AddFix("Set Compressed", () => SetCompressed(a, info),
                    $"Compression → Compressed, format → Automatic for {a.TexturePlatform}, then reimports.");
            }
            else if (!info.IsSprite && info.Npot && importer.npotScale == TextureImporterNPOTScale.None)
            {
                int potW = Mathf.ClosestPowerOfTwo(info.Width), potH = Mathf.ClosestPowerOfTwo(info.Height);
                a.Add(Severity.Info, FindingKind.NpotMaterialTexture, path,
                    $"NPOT material texture {info.Width}×{info.Height}",
                    $"Compresses fine (dims are ×4), but mipmaps and Repeat wrap on non-power-of-two textures are unsupported on WebGL 1 " +
                    $"and slower on some mobile GPUs. NPOT → ToNearest ({potW}×{potH}) is an importer setting only — UVs don't change.", 0)
                    .AddFix("POT (NPOT → ToNearest)", () => SetNpotToNearest(a, info));
            }

            if (info.Mipmaps && (info.TextureType == TextureImporterType.Sprite || info.TextureType == TextureImporterType.GUI))
            {
                a.Add(BuildAnalysis.SeverityFor(tex / 4), FindingKind.MipmapsOnSprite, path, "Mipmaps on a sprite/UI texture",
                    $"Mipmaps enabled on a {info.TextureType} texture — 33% extra size and blurrier UI.", tex / 4)
                    .AddFix("Disable mipmaps", () => DisableMipmaps(a, info));
            }

            if (!info.HasAlpha && AlphaFormatsWithRgbAlternative.Contains(info.Format))
            {
                a.Add(BuildAnalysis.SeverityFor(tex / 2), FindingKind.NoAlphaButAlphaFormat, path, $"No alpha but {info.Format}",
                    $"Source has no alpha but the build format is {info.Format}. An RGB format would halve it.", tex / 2)
                    .AddFix("Alpha Source → None", () => SetAlphaSourceNone(a, info),
                        "Sets Alpha Source to None so Automatic picks an RGB format, then reimports.");
            }

            bool crunchableFormat = info.Format.StartsWith("DXT", StringComparison.Ordinal) || info.Format.StartsWith("ETC", StringComparison.Ordinal);
            if (!info.Crunched && !notBlockCompressible && !explicitUncompressed && crunchableFormat
                && tex >= CrunchMinSize && info.TextureType != TextureImporterType.NormalMap)
            {
                long savings = tex / 2;
                Finding f = a.Add(Severity.Info, FindingKind.CrunchCandidate, path, "Crunch compression (≈ half the size)",
                    $"{Fmt.Bytes(tex)} as {info.Format}. Crunch (lossy, on top of {info.Format}) usually halves it or better — the project's " +
                    "atlases already use it. Costs a little CPU to decompress at load and some quality on gradients.", savings);
                f.IsTradeoff = true;
                f.AddFix("Enable Crunch (50)", () => EnableCrunch(a, info), "Crunch compression on, quality 50, then reimports.");
            }

            bool oversized = info.MaxDim > LargeTextureDim || (info.IsSprite && info.MaxDim > 1024 && tex >= OversizedSpriteMinSize);
            if (oversized)
            {
                int newMax = FloorPowerOfTwo(info.MaxDim);
                if (newMax == info.MaxDim) newMax /= 2;
                double ratio = (double)newMax / info.MaxDim;
                long savings = (long)(tex * (1 - ratio * ratio));
                Finding f = a.Add(Severity.Info, FindingKind.OversizedTexture, path, $"{info.MaxDim}px — Max Size {newMax}",
                    $"{info.Width}×{info.Height} ({Fmt.Bytes(tex)}). Max Size {newMax} for {a.TexturePlatform} imports it at " +
                    $"{(int)(info.Width * ratio)}×{(int)(info.Height * ratio)} (≈{Fmt.Bytes(tex - savings)}). Sprites keep their on-screen size " +
                    "(Unity rescales Pixels Per Unit), they just get softer — check it at the gameplay camera / on the device.", savings);
                f.IsTradeoff = true;
                f.AddFix($"Max Size → {newMax}", () => SetMaxSize(a, info, newMax), $"Sets Max Size to {newMax} and reimports.");
            }

            if (info.Readable)
            {
                a.Add(Severity.Info, FindingKind.ReadWrite, path, "Read/Write enabled",
                    "Read/Write enabled — a CPU copy is kept in memory at runtime (no build-size cost). Disable unless code reads its pixels.", 0);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Bytes per pixel the texture would have once it compresses on this platform.</summary>
        private static double TargetBytesPerPixel(BuildAnalysis a, TextureInfo info)
        {
            if (!UncompressedFormats.Contains(info.Format) && info.Format != "Automatic")
                return BytesPerPixelFor(info.Format, TextureImporterCompression.Compressed, a.TexturePlatform);

            if (a.TexturePlatform == "Android" || a.TexturePlatform == "iPhone") return 0.45; // ASTC 6×6
            return info.HasAlpha ? 1.0 : 0.5; // DXT5 / DXT1
        }

        private static int Ceil4(int v) => (v + 3) / 4 * 4;

        private static int FloorPowerOfTwo(int v)
        {
            int p = 1;
            while (p * 2 <= v) p *= 2;
            return p;
        }

        private static string Names(List<string> paths)
        {
            if (paths.Count == 0) return "nothing found";
            string names = string.Join(", ", paths.Take(3).Select(Path.GetFileName));
            return paths.Count > 3 ? $"{names} +{paths.Count - 3} more" : names;
        }

        private static bool IsRewritable(string path) => RewritableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        private static TextureImporter ImporterFor(string path) => AssetImporter.GetAtPath(path) as TextureImporter;

        public static void SelectAll(IEnumerable<string> paths)
        {
            UnityEngine.Object[] objects = paths.Select(AssetDatabase.LoadMainAssetAtPath).Where(o => o != null).ToArray();
            Selection.objects = objects;
            if (objects.Length > 0) EditorGUIUtility.PingObject(objects[0]);
        }

        private static void RefreshDims(TextureInfo info)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(info.Path);
            if (texture == null) return;
            info.Width = texture.width;
            info.Height = texture.height;
            info.Npot = !Mathf.IsPowerOfTwo(info.Width) || !Mathf.IsPowerOfTwo(info.Height);
        }

        /// <summary>Applies <paramref name="change"/> to the platform override when the texture has one for this platform, else to the defaults.</summary>
        private static void EditPlatformSettings(BuildAnalysis a, TextureImporter importer, Action<TextureImporterPlatformSettings> change)
        {
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(a.TexturePlatform);
            if (platform != null && platform.overridden)
            {
                change(platform);
                importer.SetPlatformTextureSettings(platform);
                return;
            }

            TextureImporterPlatformSettings defaults = importer.GetDefaultPlatformTextureSettings();
            change(defaults);
            importer.SetPlatformTextureSettings(defaults);
        }

        // ------------------------------------------------------------------ fixes

        /// <summary>
        /// Rewrites a PNG/JPG so both sides are multiples of 4 (block compression needs 4×4 blocks) — padding only,
        /// never a rescale. Sprite sheets are padded on the right/top: Unity's sprite rects are measured from the
        /// bottom-left, so they keep pointing at the same pixels. Single sprites are padded around the center and get
        /// a Custom pivot so the pivot stays on the same pixel; 9-slice borders grow by the padding.
        /// </summary>
        public static void PadToMultipleOf4(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;
            if (!IsRewritable(info.Path))
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': only PNG/JPG sources can be padded. Re-export it as PNG.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D result = null;
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(info.Path), false))
                {
                    LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': could not decode the source file.");
                    return;
                }

                int w = source.width, h = source.height;
                int pw = Ceil4(w), ph = Ceil4(h);
                if (pw == w && ph == h)
                {
                    LogHelper.Log(BuildAnalysis.LogTag, $"'{info.Name}': the source is already {w}×{h} (×4). The imported {info.Width}×{info.Height} comes from Max Size — " +
                                                        "pick a Max Size that keeps it a multiple of 4.");
                    return;
                }

                bool single = info.IsSprite && importer.spriteImportMode == SpriteImportMode.Single;
                int ox = single ? (pw - w) / 2 : 0;
                int oy = single ? (ph - h) / 2 : 0;

                Vector2 pivotPx = new Vector2(w * 0.5f, h * 0.5f);
                if (single)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(info.Path);
                    if (sprite != null && sprite.rect.width > 0 && sprite.rect.height > 0)
                        pivotPx = new Vector2(sprite.pivot.x * w / sprite.rect.width, sprite.pivot.y * h / sprite.rect.height);
                }

                Color32[] src = source.GetPixels32();
                var dst = new Color32[pw * ph]; // transparent black
                for (int y = 0; y < h; y++)
                    Array.Copy(src, y * w, dst, (y + oy) * pw + ox, w);

                result = new Texture2D(pw, ph, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels32(dst);
                result.Apply(false, false);

                string ext = Path.GetExtension(info.Path).ToLowerInvariant();
                File.WriteAllBytes(info.Path, ext == ".png" ? result.EncodeToPNG() : result.EncodeToJPG(95));

                if (single)
                {
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = new Vector2((pivotPx.x + ox) / pw, (pivotPx.y + oy) / ph);
                    Vector4 b = settings.spriteBorder;
                    if (b != Vector4.zero)
                        settings.spriteBorder = new Vector4(b.x + ox, b.y + oy, b.z + (pw - w - ox), b.w + (ph - h - oy));
                    importer.SetTextureSettings(settings);
                }

                if (info.Compression == TextureImporterCompression.Uncompressed)
                    EditPlatformSettings(a, importer, p => p.textureCompression = TextureImporterCompression.Compressed);

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(info.Path, ImportAssetOptions.ForceUpdate);
                RefreshDims(info);
                a.Resolve(info.Path, FindingKind.NotBlockCompressible, FindingKind.Uncompressed);

                LogHelper.Log(BuildAnalysis.LogTag,
                    $"'{info.Name}': padded {w}×{h} → {pw}×{ph} ({(single ? "centered, pivot kept" : "bottom-left anchored, sprite rects unchanged")}, " +
                    $"{(ext == ".png" ? "transparent" : "black — JPG has no alpha")} padding). The file was rewritten; revert with git if unwanted.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
            }
        }

        /// <summary>Importer Non-Power-of-2 → ToNearest: non-destructive, UVs unchanged. For material/texture assets only (ignored on sprites).</summary>
        public static void SetNpotToNearest(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            if (info.Compression == TextureImporterCompression.Uncompressed)
                EditPlatformSettings(a, importer, p => p.textureCompression = TextureImporterCompression.Compressed);
            importer.SaveAndReimport();
            RefreshDims(info);
            a.Resolve(info.Path, FindingKind.NotBlockCompressible, FindingKind.NpotMaterialTexture, FindingKind.Uncompressed);
            LogHelper.Log(BuildAnalysis.LogTag, $"'{info.Name}': Non-Power-of-2 → ToNearest, now imports as {info.Width}×{info.Height}.");
        }

        /// <summary>What <see cref="MakePowerOfTwoKeepingSlices"/> would do, for tooltips.</summary>
        public static string MakePotDescription(TextureInfo info)
        {
            PotPlan(info.Width, info.Height, out float scale, out int sw, out int sh, out int pw, out int ph);
            return $"Scales {info.Width}×{info.Height} uniformly ×{scale:0.###} to {sw}×{sh}, pads to {pw}×{ph} (POT), scales every slice rect / " +
                   "9-slice border by the same factor and Pixels Per Unit too — sprites keep their shape and world size. Bigger than Pad to ×4; " +
                   "only worth it when something really needs POT. Rewrites the file (revert with git).";
        }

        /// <summary>Uniform scale so the larger side lands on its closest power of two, then pad each side up to a power of two.</summary>
        private static void PotPlan(int w, int h, out float scale, out int sw, out int sh, out int pw, out int ph)
        {
            int max = Math.Max(1, Math.Max(w, h));
            scale = (float)Mathf.ClosestPowerOfTwo(max) / max;
            sw = Math.Max(1, Mathf.RoundToInt(w * scale));
            sh = Math.Max(1, Mathf.RoundToInt(h * scale));
            pw = Mathf.NextPowerOfTwo(sw);
            ph = Mathf.NextPowerOfTwo(sh);
        }

        /// <summary>
        /// Makes a sprite texture power-of-two without breaking its slices: the art is scaled UNIFORMLY (never a
        /// different factor per axis, so nothing gets squashed) so the larger side lands on its closest POT, then
        /// padded with transparent pixels on the right/top up to POT. Every sprite rect and 9-slice border is scaled by
        /// the same factor (rects are bottom-left based, so the padding never moves them; pivots are normalized and stay
        /// valid) and Pixels Per Unit is multiplied by it, so every sprite keeps its exact world size.
        /// </summary>
        public static void MakePowerOfTwoKeepingSlices(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;
            if (!IsRewritable(info.Path))
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': only PNG/JPG sources can be rewritten. Re-export it as PNG.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D result = null;
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(info.Path), false))
                {
                    LogHelper.Error(BuildAnalysis.LogTag, $"'{info.Name}': could not decode the source file.");
                    return;
                }

                int w = source.width, h = source.height;
                PotPlan(w, h, out float scale, out int sw, out int sh, out int pw, out int ph);
                if (pw == w && ph == h)
                {
                    LogHelper.Log(BuildAnalysis.LogTag, $"'{info.Name}': the source is already {w}×{h} (POT).");
                    return;
                }

                // Rects are read before the file changes; the sprite provider works in source pixels.
                var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
                factory.Init();
                UnityEditor.U2D.Sprites.ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                provider?.InitSpriteEditorDataProvider();
                SpriteRect[] rects = provider?.GetSpriteRects() ?? Array.Empty<SpriteRect>();
                bool single = importer.spriteImportMode == SpriteImportMode.Single;

                Color32[] scaled = Mathf.Approximately(scale, 1f)
                    ? source.GetPixels32()
                    : ImageResampler.Resample(source.GetPixels32(), w, h, sw, sh);
                var dst = new Color32[pw * ph]; // transparent black
                for (int y = 0; y < sh; y++)
                    Array.Copy(scaled, y * sw, dst, y * pw, sw);

                result = new Texture2D(pw, ph, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels32(dst);
                result.Apply(false, false);

                string ext = Path.GetExtension(info.Path).ToLowerInvariant();
                File.WriteAllBytes(info.Path, ext == ".png" ? result.EncodeToPNG() : result.EncodeToJPG(95));

                if (single)
                {
                    // The single sprite now covers the padded texture: pin its pivot to the same art pixel and grow the
                    // 9-slice borders by the padding so the stretchable area is unchanged.
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(info.Path);
                    Vector2 pivotPx = sprite != null && sprite.rect.width > 0
                        ? new Vector2(sprite.pivot.x * w / sprite.rect.width, sprite.pivot.y * h / sprite.rect.height)
                        : new Vector2(w * 0.5f, h * 0.5f);
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = new Vector2(pivotPx.x * scale / pw, pivotPx.y * scale / ph);
                    Vector4 b = settings.spriteBorder;
                    if (b != Vector4.zero)
                        settings.spriteBorder = new Vector4(Mathf.Round(b.x * scale), Mathf.Round(b.y * scale),
                            Mathf.Round(b.z * scale) + (pw - sw), Mathf.Round(b.w * scale) + (ph - sh));
                    settings.spritePixelsPerUnit *= scale;
                    importer.SetTextureSettings(settings);
                }
                else if (provider != null && rects.Length > 0)
                {
                    foreach (SpriteRect rect in rects)
                    {
                        Rect r = rect.rect;
                        rect.rect = new Rect(Mathf.Round(r.x * scale), Mathf.Round(r.y * scale),
                            Mathf.Max(1f, Mathf.Round(r.width * scale)), Mathf.Max(1f, Mathf.Round(r.height * scale)));
                        Vector4 b = rect.border;
                        rect.border = new Vector4(Mathf.Round(b.x * scale), Mathf.Round(b.y * scale), Mathf.Round(b.z * scale), Mathf.Round(b.w * scale));
                        // rect.pivot is normalized to the rect — unchanged by a uniform scale.
                    }

                    provider.SetSpriteRects(rects);
                    provider.Apply();
                    importer.spritePixelsPerUnit *= scale;
                }
                else
                {
                    importer.spritePixelsPerUnit *= scale;
                }

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(info.Path, ImportAssetOptions.ForceUpdate);
                RefreshDims(info);
                a.Resolve(info.Path, FindingKind.NotBlockCompressible, FindingKind.Uncompressed);

                LogHelper.Log(BuildAnalysis.LogTag,
                    $"'{info.Name}': {w}×{h} → scaled ×{scale:0.###} to {sw}×{sh} → padded to {pw}×{ph}. " +
                    $"{(single ? "Pivot pinned, borders kept" : $"{rects.Length} slice rect(s) scaled")}, Pixels Per Unit ×{scale:0.###} " +
                    $"(now {importer.spritePixelsPerUnit:0.##}) so world size is unchanged. The file was rewritten; revert with git if unwanted.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
            }
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

        private static void EnableCrunch(BuildAnalysis a, TextureInfo info)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            EditPlatformSettings(a, importer, p =>
            {
                p.crunchedCompression = true;
                p.compressionQuality = 50;
            });
            importer.SaveAndReimport();
            info.Crunched = true;
            a.Resolve(info.Path, FindingKind.CrunchCandidate);
            LogHelper.Log(BuildAnalysis.LogTag, $"Enabled Crunch (quality 50) on '{info.Path}'.");
        }

        private static void SetMaxSize(BuildAnalysis a, TextureInfo info, int maxSize)
        {
            TextureImporter importer = ImporterFor(info.Path);
            if (importer == null) return;

            EditPlatformSettings(a, importer, p => p.maxTextureSize = maxSize);
            importer.SaveAndReimport();
            info.MaxSize = maxSize;
            RefreshDims(info);
            a.Resolve(info.Path, FindingKind.OversizedTexture);
            LogHelper.Log(BuildAnalysis.LogTag, $"Max Size → {maxSize} on '{info.Path}' (now {info.Width}×{info.Height}).");
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
