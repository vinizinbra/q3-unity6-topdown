using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.EditorTools.SpriteOptimizer;
using UnityEditor;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Who references each shipped texture, and how: read straight from the YAML of the shipped assets and the
    /// Build Settings scenes. A reference to <c>fileID: 2800000</c> is the Texture2D itself (materials, RawImages,
    /// TMP sprite assets, Texture fields) — that is what makes Unity write the texture into the build even when
    /// it is packed in an atlas. Any other fileID is one of its Sprites (served from the atlas when packed).
    /// Sprite atlases themselves are skipped (they always reference the texture).
    /// </summary>
    internal sealed class TextureReferenceIndex
    {
        public const long TextureFileId = 2800000;

        public readonly Dictionary<string, List<string>> TextureRefs = new Dictionary<string, List<string>>();
        public readonly Dictionary<string, List<string>> SpriteRefs = new Dictionary<string, List<string>>();
        /// <summary>Per texture: how many sprite references look like UI / gameplay (component type, then field name).</summary>
        public readonly Dictionary<string, int> UiVotes = new Dictionary<string, int>();
        public readonly Dictionary<string, int> GameplayVotes = new Dictionary<string, int>();
        public int ScannedFiles;

        private static readonly HashSet<string> YamlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mat", ".prefab", ".unity", ".asset", ".anim", ".controller", ".overridecontroller", ".playable",
            ".guiskin", ".fontsettings", ".mixer", ".lighting", ".terrainlayer", ".brush", ".flare", ".renderTexture",
        };

        public static IReadOnlyList<string> Of(Dictionary<string, List<string>> map, string path) =>
            map.TryGetValue(path, out List<string> list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        public static TextureReferenceIndex Build(BuildAnalysis a, ProgressCallback progress)
        {
            var index = new TextureReferenceIndex();

            var shippedTextures = new Dictionary<string, string>(); // path → guid
            foreach (AssetEntry e in a.Assets)
            {
                if (e.Category != AssetCategory.Texture || !e.IsUnderAssets) continue;
                string guid = AssetDatabase.AssetPathToGUID(e.Path);
                if (!string.IsNullOrEmpty(guid)) shippedTextures[e.Path] = guid;
            }

            var referencers = new HashSet<string>(a.Assets
                .Where(e => e.IsUnderAssets && YamlExtensions.Contains(Path.GetExtension(e.Path)))
                .Select(e => e.Path));
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) referencers.Add(scene.path);

            List<string> files = referencers.OrderBy(p => p, StringComparer.Ordinal).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                if (i % 25 == 0)
                    progress($"Indexing texture references ({i + 1}/{files.Count})…", 0.2f + 0.1f * i / Math.Max(1, files.Count));

                string[] deps;
                try { deps = AssetDatabase.GetDependencies(path, false); }
                catch { continue; }

                List<string> candidates = null;
                foreach (string dep in deps)
                {
                    if (dep == path || !shippedTextures.ContainsKey(dep)) continue;
                    (candidates ?? (candidates = new List<string>())).Add(dep);
                }

                if (candidates == null) continue;

                string text = ReadYaml(path);
                if (text == null) continue;
                index.ScannedFiles++;

                foreach (string texturePath in candidates)
                {
                    Classify(text, shippedTextures[texturePath], out bool texture, out bool sprite, out int ui, out int gameplay);
                    if (texture) AddTo(index.TextureRefs, texturePath, path);
                    if (sprite) AddTo(index.SpriteRefs, texturePath, path);
                    if (ui > 0) index.UiVotes[texturePath] = index.UiVotes.TryGetValue(texturePath, out int u) ? u + ui : ui;
                    if (gameplay > 0) index.GameplayVotes[texturePath] = index.GameplayVotes.TryGetValue(texturePath, out int g) ? g + gameplay : gameplay;
                }
            }

            return index;
        }

        private static string ReadYaml(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var header = new byte[5];
                    if (stream.Read(header, 0, 5) < 5 || header[0] != '%' || header[1] != 'Y') return null; // binary-serialized
                }

                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Looks at every "fileID: N, guid: G" pair for this guid. Sprite references also vote UI vs gameplay: by the
        /// component they sit on (SpriteRenderer / ParticleSystem / SpriteMask → gameplay, an Image's m_Sprite → UI),
        /// else by the field name (Sprite Optimizer's rule: icon/ui/portrait… → UI, body/projectile/pawn… → gameplay).
        /// </summary>
        private static void Classify(string text, string guid, out bool texture, out bool sprite, out int ui, out int gameplay)
        {
            texture = sprite = false;
            ui = gameplay = 0;
            int at = 0;
            while ((at = text.IndexOf(guid, at, StringComparison.Ordinal)) >= 0)
            {
                long fileId = FileIdBefore(text, at);
                if (fileId == TextureFileId)
                {
                    texture = true;
                }
                else if (fileId != 0)
                {
                    sprite = true;
                    switch (VoteFor(text, at))
                    {
                        case UsageContext.UI: ui++; break;
                        case UsageContext.Gameplay: gameplay++; break;
                    }
                }
                at += guid.Length;
            }
        }

        private static UsageContext VoteFor(string text, int at)
        {
            int classId = ClassIdBefore(text, at);
            if (classId == 212 || classId == 198 || classId == 199 || classId == 331) return UsageContext.Gameplay;

            string field = FieldNameAt(text, at);
            if (classId == 114 && (field == "m_Sprite" || field == "m_OverrideSprite")) return UsageContext.UI;
            if (string.IsNullOrEmpty(field) || field.StartsWith("m_", StringComparison.Ordinal)) return UsageContext.Unknown;

            switch (SpriteUsageScanner.GuessFromFieldName(field))
            {
                case SpriteContext.UI: return UsageContext.UI;
                case SpriteContext.Gameplay: return UsageContext.Gameplay;
                default: return UsageContext.Unknown;
            }
        }

        /// <summary>Unity class id of the YAML document containing <paramref name="at"/> ("--- !u!212 &…" → 212).</summary>
        private static int ClassIdBefore(string text, int at)
        {
            int header = text.LastIndexOf("--- !u!", at, StringComparison.Ordinal);
            if (header < 0) return 0;
            int i = header + 7, end = i;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return int.TryParse(text.Substring(i, end - i), out int id) ? id : 0;
        }

        /// <summary>The YAML key holding the reference — for list items ("- {fileID…}") the key of the enclosing list.</summary>
        private static string FieldNameAt(string text, int at)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            string line = text.Substring(lineStart, at - lineStart).TrimStart();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                int colon = line.IndexOf(':');
                return colon > 0 ? line.Substring(0, colon).Trim() : null;
            }

            // Walk up to the list's key line ("Frames:").
            for (int end = lineStart - 1, guard = 0; end > 0 && guard < 200; guard++)
            {
                int start = text.LastIndexOf('\n', end - 1) + 1;
                string previous = text.Substring(start, end - start).Trim();
                if (previous.EndsWith(":", StringComparison.Ordinal) && !previous.StartsWith("-", StringComparison.Ordinal))
                    return previous.Substring(0, previous.Length - 1);
                end = start - 1;
            }

            return null;
        }

        private static long FileIdBefore(string text, int guidIndex)
        {
            int start = Math.Max(0, guidIndex - 48);
            int key = text.LastIndexOf("fileID: ", guidIndex, guidIndex - start, StringComparison.Ordinal);
            if (key < 0) return 0;

            int i = key + "fileID: ".Length;
            int end = i;
            while (end < guidIndex && (char.IsDigit(text[end]) || text[end] == '-')) end++;
            return long.TryParse(text.Substring(i, end - i), out long id) ? id : 0;
        }

        private static void AddTo(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out List<string> list))
            {
                list = new List<string>();
                map.Add(key, list);
            }

            if (!list.Contains(value)) list.Add(value);
        }
    }
}
