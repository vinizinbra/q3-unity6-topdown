using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Finds byte-identical source files that shipped under different paths (typical: a copy under
    /// Assets/0_Refs next to the real one under Assets/_Project). Files are grouped by length first so
    /// only real candidates are hashed.
    /// </summary>
    internal static class DuplicateContentAudit
    {
        private const long MinFileBytes = 4 * 1024;

        public static void Run(BuildAnalysis a, ProgressCallback progress)
        {
            progress("Looking for duplicate files…", 0.85f);

            var byLength = new Dictionary<long, List<AssetEntry>>();
            foreach (AssetEntry entry in a.Assets)
            {
                if (!entry.IsUnderAssets) continue;
                if (entry.Category != AssetCategory.Texture && entry.Category != AssetCategory.Audio
                    && entry.Category != AssetCategory.Mesh && entry.Category != AssetCategory.Video)
                    continue;

                long length;
                try
                {
                    var file = new FileInfo(entry.Path);
                    if (!file.Exists || file.Length < MinFileBytes) continue;
                    length = file.Length;
                }
                catch
                {
                    continue;
                }

                if (!byLength.TryGetValue(length, out List<AssetEntry> list))
                {
                    list = new List<AssetEntry>();
                    byLength.Add(length, list);
                }

                list.Add(entry);
            }

            List<List<AssetEntry>> candidates = byLength.Values.Where(l => l.Count >= 2).ToList();
            int done = 0;
            int total = candidates.Sum(l => l.Count);

            foreach (List<AssetEntry> group in candidates)
            {
                var byHash = new Dictionary<string, List<AssetEntry>>();
                foreach (AssetEntry entry in group)
                {
                    done++;
                    if (done % 5 == 0)
                        progress($"Hashing candidate files ({done}/{total})…", 0.85f + 0.13f * done / Math.Max(1, total));

                    string hash = HashFile(entry.Path);
                    if (hash == null) continue;

                    if (!byHash.TryGetValue(hash, out List<AssetEntry> same))
                    {
                        same = new List<AssetEntry>();
                        byHash.Add(hash, same);
                    }

                    same.Add(entry);
                }

                foreach (List<AssetEntry> same in byHash.Values.Where(l => l.Count >= 2))
                    EmitGroup(a, same);
            }
        }

        private static void EmitGroup(BuildAnalysis a, List<AssetEntry> same)
        {
            // Prefer keeping the copy that lives in the project folder; otherwise the first by path.
            AssetEntry keep = same.OrderByDescending(e => e.IsProjectAsset)
                .ThenBy(e => e.IsSuspiciousSource)
                .ThenBy(e => e.Path, StringComparer.Ordinal)
                .First();

            string[] allPaths = same.Select(e => e.Path).ToArray();

            foreach (AssetEntry extra in same.Where(e => e != keep))
            {
                string others = string.Join(", ", same.Where(e => e != extra).Select(e => e.Path));
                Finding f = a.Add(Severity.Warning, FindingKind.DuplicateContent, extra.Path,
                    $"Byte-identical to {others}. Both copies ship ({Fmt.Bytes(extra.TotalPackedSize)} each). " +
                    $"Repoint references to '{keep.Path}' and delete this one.",
                    extra.TotalPackedSize);
                f.RelatedPaths = allPaths;
                f.AddFix("Select all copies", () => SelectAll(allPaths), "Selects every identical copy in the Project window.");
            }
        }

        private static void SelectAll(string[] paths)
        {
            Object[] objects = paths.Select(AssetDatabase.LoadMainAssetAtPath).Where(o => o != null).ToArray();
            Selection.objects = objects;
            if (objects.Length > 0) EditorGUIUtility.PingObject(objects[0]);
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
