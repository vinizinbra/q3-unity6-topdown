using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Reads every SpriteAtlas in the project (not only the two known ones), records which textures each
    /// packs, and reports duplicate entries, textures packed in several atlases, suspicious sources and
    /// non-sprite packables. <see cref="AtlasActions"/> holds the one-click fixes.
    /// </summary>
    internal static class AtlasAudit
    {
        private const string DefaultPlatformName = "DefaultTexturePlatform";

        /// <summary>First pass: build atlas reports and the texture → atlases membership map.</summary>
        public static void Collect(BuildAnalysis a, ProgressCallback progress)
        {
            string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                progress($"Scanning atlas {System.IO.Path.GetFileNameWithoutExtension(path)}…", 0.15f + 0.1f * i / Math.Max(1, guids.Length));

                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null) continue;

                AtlasReport report = Describe(a, atlas, path);
                a.Atlases.Add(report);

                foreach (string texturePath in report.UniqueTexturePaths)
                {
                    if (!a.AtlasMembership.TryGetValue(texturePath, out List<AtlasReport> list))
                    {
                        list = new List<AtlasReport>();
                        a.AtlasMembership.Add(texturePath, list);
                    }

                    if (!list.Contains(report))
                        list.Add(report);
                }
            }

            // Cross-link "also in" now that every atlas is known.
            foreach (AtlasReport report in a.Atlases)
            foreach (AtlasPackableInfo packable in report.Packables)
            {
                if (a.AtlasMembership.TryGetValue(packable.Path, out List<AtlasReport> list))
                    packable.AlsoIn.AddRange(list.Where(r => r != report));
            }

            a.Atlases.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        }

        private static AtlasReport Describe(BuildAnalysis a, SpriteAtlas atlas, string path)
        {
            var report = new AtlasReport
            {
                Path = path,
                Name = atlas.name,
                Atlas = atlas,
            };

            Object[] packables = atlas.GetPackables() ?? Array.Empty<Object>();
            report.RawPackableCount = packables.Length;
            report.DistinctPackableCount = packables.Where(p => p != null).Distinct().Count();

            var texturePaths = new List<string>();
            foreach (Object packable in packables.Where(p => p != null).Distinct())
            {
                string packablePath = AssetDatabase.GetAssetPath(packable);
                if (string.IsNullOrEmpty(packablePath)) continue;

                if (packable is Texture2D || packable is Sprite)
                {
                    texturePaths.Add(packablePath);
                }
                else if (AssetDatabase.IsValidFolder(packablePath))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { packablePath }))
                        texturePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else
                {
                    texturePaths.Add(packablePath);
                }
            }

            report.UniqueTexturePaths.AddRange(texturePaths.Distinct().OrderBy(p => p, StringComparer.Ordinal));

            try
            {
                report.IncludeInBuild = atlas.IsIncludeInBuild();
                report.Packing = atlas.GetPackingSettings();
                report.TextureSettings = atlas.GetTextureSettings();

                TextureImporterPlatformSettings platform = atlas.GetPlatformSettings(a.TexturePlatform);
                report.PlatformOverridden = platform != null && platform.overridden;
                if (!report.PlatformOverridden)
                    platform = atlas.GetPlatformSettings(DefaultPlatformName);
                report.PlatformSettings = platform;
            }
            catch (Exception e)
            {
                LogHelper.Warn(BuildAnalysis.LogTag, $"Could not read settings of atlas '{path}': {e.Message}");
            }

            if (report.PlatformSettings != null)
            {
                report.EstimatedBytesPerPixel = TextureAudit.BytesPerPixelFor(
                    report.PlatformSettings.format.ToString(), report.PlatformSettings.textureCompression, a.TexturePlatform);
            }

            AssetEntry entry = a.EntryFor(path);
            report.PackedSizeInBuild = entry?.TotalPackedSize ?? 0;

            Texture2D[] pages = GetPreviewTextures(atlas);
            if (pages != null && pages.Length > 0)
            {
                report.PageCount = pages.Length;
                report.PageArea = pages.Where(p => p != null).Sum(p => (long)p.width * p.height);
                report.PageDims = string.Join(", ", pages.Where(p => p != null).Select(p => $"{p.width}×{p.height}"));
            }

            foreach (string texturePath in report.UniqueTexturePaths)
            {
                var info = new AtlasPackableInfo
                {
                    Path = texturePath,
                    Suspicious = BuildAnalysis.IsSuspiciousSourcePath(texturePath),
                };

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (texture == null)
                {
                    info.Missing = true;
                }
                else
                {
                    info.Width = texture.width;
                    info.Height = texture.height;
                    report.PackableArea += info.Area;
                }

                if (importer != null)
                {
                    info.TextureType = importer.textureType;
                    info.IsSprite = importer.textureType == TextureImporterType.Sprite;
                }

                report.Packables.Add(info);
            }

            return report;
        }

        /// <summary>Second pass (after the texture audit): findings.</summary>
        public static void Emit(BuildAnalysis a)
        {
            foreach (AtlasReport report in a.Atlases)
            {
                if (report.DuplicateEntryCount > 0)
                {
                    a.Add(Severity.Warning, FindingKind.DuplicateAtlasEntries, report.Path,
                        $"{report.RawPackableCount} packable entries but only {report.DistinctPackableCount} distinct objects " +
                        $"({report.DuplicateEntryCount} duplicates). Slows packing and hides what the atlas really contains.")
                        .AddFix("Remove duplicate entries", () => AtlasActions.Dedupe(a, report));
                }

                if (!report.IncludeInBuild)
                {
                    a.Add(Severity.Info, FindingKind.AtlasNotInBuild, report.Path,
                        "Include in Build is off — sprites are loaded from their source textures at runtime.");
                }

                if (report.PlatformSettings != null)
                {
                    string format = report.PlatformSettings.format.ToString();
                    if (report.PlatformSettings.textureCompression == TextureImporterCompression.Uncompressed
                        || format == "RGBA32" || format == "ARGB32" || format == "RGB24")
                    {
                        a.Add(Severity.Warning, FindingKind.AtlasUncompressed, report.Path,
                            $"Atlas is uncompressed for {a.TexturePlatform} ({format}, {report.PlatformSettings.textureCompression}).",
                            report.PackedSizeInBuild - report.PackedSizeInBuild / 6);
                    }
                }

                if (report.PageCount > 0 && report.FillRatio >= 0 && report.FillRatio < 0.5)
                {
                    a.Add(Severity.Info, FindingKind.AtlasPoorlyFilled, report.Path,
                        $"Atlas pages ({report.PageDims}) are only {report.FillRatio:P0} covered by sprite pixels. " +
                        "Consider a smaller Max Texture Size, tight packing, or rotation.");
                }

                foreach (AtlasPackableInfo packable in report.Packables)
                {
                    if (packable.Missing)
                    {
                        a.Add(Severity.Warning, FindingKind.NonSpritePackable, packable.Path,
                            $"Packable of {report.Name} could not be loaded as a Texture2D.")
                            .AddFix($"Remove from {report.Name}", () => AtlasActions.RemoveFromAtlas(a, report, packable.Path));
                        continue;
                    }

                    if (!packable.IsSprite)
                    {
                        a.Add(Severity.Warning, FindingKind.NonSpritePackable, packable.Path,
                            $"Packed in {report.Name} but its Texture Type is {packable.TextureType}, not Sprite — the atlas ignores it.")
                            .AddFix($"Remove from {report.Name}", () => AtlasActions.RemoveFromAtlas(a, report, packable.Path));
                    }

                    if (packable.Suspicious)
                    {
                        a.Add(Severity.Warning, FindingKind.SuspiciousAtlasPackable, packable.Path,
                            $"Packed in {report.Name} from a reference/demo/delete folder. Move the texture into Assets/_Project or drop it.")
                            .AddFix($"Remove from {report.Name}", () => AtlasActions.RemoveFromAtlas(a, report, packable.Path));
                    }
                }
            }

            foreach (KeyValuePair<string, List<AtlasReport>> kv in a.AtlasMembership)
            {
                if (kv.Value.Count < 2) continue;

                string texturePath = kv.Key;
                List<AtlasReport> atlases = kv.Value;
                AtlasPackableInfo any = atlases[0].Packables.FirstOrDefault(p => p.Path == texturePath);
                long area = any?.Area ?? 0;
                long savings = 0;
                for (int i = 1; i < atlases.Count; i++)
                    savings += (long)(area * atlases[i].EstimatedBytesPerPixel);

                Finding f = a.Add(Severity.Error, FindingKind.InMultipleAtlases, texturePath,
                    $"Packed in {atlases.Count} atlases ({string.Join(", ", atlases.Select(r => r.Name))}) — its pixels ship once per atlas. Keep it in one.",
                    savings);

                foreach (AtlasReport keep in atlases)
                {
                    AtlasReport keepLocal = keep;
                    f.AddFix($"Keep in {keep.Name}", () =>
                    {
                        foreach (AtlasReport other in atlases.Where(r => r != keepLocal).ToList())
                            AtlasActions.RemoveFromAtlas(a, other, texturePath);
                    }, $"Removes the texture from every atlas except {keep.Name}.");
                }
            }
        }

        private static Texture2D[] GetPreviewTextures(SpriteAtlas atlas)
        {
            try
            {
                foreach (Type type in new[] { typeof(SpriteAtlasExtensions), typeof(SpriteAtlasUtility) })
                {
                    MethodInfo method = type.GetMethod("GetPreviewTextures",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(SpriteAtlas) }, null);
                    if (method != null)
                        return method.Invoke(null, new object[] { atlas }) as Texture2D[];
                }
            }
            catch (Exception e)
            {
                LogHelper.Warn(BuildAnalysis.LogTag, $"Could not read packed pages of '{atlas.name}': {e.Message}");
            }

            return null;
        }
    }

    /// <summary>One-click atlas fixes. All of them keep the in-memory analysis in sync so no re-run is needed.</summary>
    internal static class AtlasActions
    {
        public static void AddToAtlas(BuildAnalysis a, TextureInfo info, string atlasPath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(info.Path);
            if (texture == null)
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"Could not load '{info.Path}' as a Texture2D.");
                return;
            }

            int added = AddTextureToAtlasContextMenu.AddTexturesToAtlas(atlasPath, new[] { texture });
            if (added < 0) return;

            AtlasReport report = a.Atlases.FirstOrDefault(r => r.Path == atlasPath);
            if (report != null && !info.InAtlases.Contains(report))
            {
                info.InAtlases.Add(report);
                report.RawPackableCount++;
                report.DistinctPackableCount++;
                report.UniqueTexturePaths.Add(info.Path);
                var packable = new AtlasPackableInfo
                {
                    Path = info.Path,
                    Width = info.Width,
                    Height = info.Height,
                    TextureType = info.TextureType,
                    IsSprite = info.IsSprite,
                    Suspicious = info.Entry.IsSuspiciousSource,
                };
                packable.AlsoIn.AddRange(info.InAtlases.Where(r => r != report));
                report.Packables.Add(packable);
                report.PackableArea += packable.Area;

                if (!a.AtlasMembership.TryGetValue(info.Path, out List<AtlasReport> list))
                {
                    list = new List<AtlasReport>();
                    a.AtlasMembership.Add(info.Path, list);
                }
                if (!list.Contains(report)) list.Add(report);
            }

            a.Resolve(info.Path, FindingKind.NotInAtlas);

            string atlasName = report?.Name ?? System.IO.Path.GetFileNameWithoutExtension(atlasPath);
            LogHelper.Log(BuildAnalysis.LogTag, added == 0
                ? $"'{info.Name}' was already in {atlasName}."
                : $"Added '{info.Name}' to {atlasName}." + (info.IsSprite ? "" : " Note: its Texture Type is not Sprite, so the atlas will ignore it until that is changed."));
        }

        public static void RemoveFromAtlas(BuildAnalysis a, AtlasReport report, string texturePath)
        {
            int removed = RewritePackables(report.Atlas, keep: p => AssetDatabase.GetAssetPath(p) != texturePath);
            if (removed < 0) return;

            report.RawPackableCount = Math.Max(0, report.RawPackableCount - removed);
            report.DistinctPackableCount = Math.Max(0, report.DistinctPackableCount - Math.Min(1, removed));
            report.UniqueTexturePaths.Remove(texturePath);
            AtlasPackableInfo packable = report.Packables.FirstOrDefault(p => p.Path == texturePath);
            if (packable != null)
            {
                report.Packables.Remove(packable);
                report.PackableArea -= packable.Area;
            }

            foreach (AtlasPackableInfo other in report.Packables) other.AlsoIn.Remove(report);
            foreach (AtlasReport otherAtlas in a.Atlases)
            {
                AtlasPackableInfo twin = otherAtlas.Packables.FirstOrDefault(p => p.Path == texturePath);
                twin?.AlsoIn.Remove(report);
            }

            if (a.AtlasMembership.TryGetValue(texturePath, out List<AtlasReport> list))
            {
                list.Remove(report);
                if (list.Count == 0) a.AtlasMembership.Remove(texturePath);
                if (list.Count < 2) a.Resolve(texturePath, FindingKind.InMultipleAtlases);
            }

            if (a.TextureByPath.TryGetValue(texturePath, out TextureInfo info))
                info.InAtlases.Remove(report);

            foreach (Finding f in a.Findings)
            {
                if (f.AssetPath == texturePath && !f.Resolved
                    && (f.Kind == FindingKind.SuspiciousAtlasPackable || f.Kind == FindingKind.NonSpritePackable)
                    && f.Message.Contains(report.Name))
                    f.Resolved = true;
            }

            LogHelper.Log(BuildAnalysis.LogTag, $"Removed '{System.IO.Path.GetFileName(texturePath)}' from {report.Name} ({removed} entries).");
        }

        public static void Dedupe(BuildAnalysis a, AtlasReport report)
        {
            var seen = new HashSet<Object>();
            int removed = RewritePackables(report.Atlas, keep: p => seen.Add(p));
            if (removed < 0) return;

            report.RawPackableCount = report.DistinctPackableCount;
            a.Resolve(report.Path, FindingKind.DuplicateAtlasEntries);
            LogHelper.Log(BuildAnalysis.LogTag, $"Removed {removed} duplicate packable entries from {report.Name}.");
        }

        /// <summary>
        /// Rewrites the atlas packable list keeping only entries for which <paramref name="keep"/> returns true.
        /// Works on the serialized list so duplicated references are handled exactly; falls back to the
        /// SpriteAtlasExtensions API if the property is not found. Returns the number of removed entries.
        /// </summary>
        private static int RewritePackables(SpriteAtlas atlas, Func<Object, bool> keep)
        {
            if (atlas == null) return -1;

            var so = new SerializedObject(atlas);
            SerializedProperty list = so.FindProperty("m_EditorData.packables");
            int removed;

            if (list != null && list.isArray)
            {
                var kept = new List<Object>();
                for (int i = 0; i < list.arraySize; i++)
                {
                    Object obj = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (obj != null && keep(obj))
                        kept.Add(obj);
                }

                removed = list.arraySize - kept.Count;
                list.arraySize = kept.Count;
                for (int i = 0; i < kept.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];

                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Object[] all = atlas.GetPackables() ?? Array.Empty<Object>();
                Object[] kept = all.Where(p => p != null && keep(p)).Distinct().ToArray();
                removed = all.Length - kept.Length;
                if (all.Length > 0) atlas.Remove(all);
                if (kept.Length > 0) atlas.Add(kept);
            }

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            return removed;
        }
    }
}
