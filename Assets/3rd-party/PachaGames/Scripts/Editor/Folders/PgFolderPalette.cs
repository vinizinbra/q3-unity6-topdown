using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    internal static class PgFolderPalette
    {
        public static readonly Color[] Colors =
        {
            Hex("FF6B60"), Hex("FF9F45"), Hex("FFD34D"), Hex("A8D95A"), Hex("5FD36B"), Hex("45CFC0"),
            Hex("59A5FF"), Hex("8A80FF"), Hex("C07DF0"), Hex("FF7FC4"), Hex("C08E63"), Hex("A9B0BA")
        };

        // Ordered specific to generic: the first entry a type is assignable to wins.
        private static readonly (string Name, Type Type)[] GlyphCatalog =
        {
            ("Script", typeof(MonoScript)),
            ("Scene", typeof(SceneAsset)),
            ("Prefab", typeof(GameObject)),
            ("Material", typeof(Material)),
            ("Shader", typeof(Shader)),
            ("Sprite", typeof(Sprite)),
            ("Texture", typeof(Texture2D)),
            ("Audio", typeof(AudioClip)),
            ("Animation", typeof(AnimationClip)),
            ("Animator", typeof(RuntimeAnimatorController)),
            ("Font", typeof(Font)),
            ("Model", typeof(Mesh)),
            ("Data", typeof(ScriptableObject))
        };

        private static readonly Dictionary<string, Texture> GlyphByName = new Dictionary<string, Texture>();
        private static readonly Dictionary<string, Texture> AutoGlyphByPath = new Dictionary<string, Texture>();
        private static readonly Dictionary<string, bool> EmptyByPath = new Dictionary<string, bool>();

        private static Texture _folderTexture;
        private static Texture _emptyFolderTexture;

        public static IEnumerable<string> GlyphNames => GlyphCatalog.Select(entry => entry.Name);

        public static Texture DefaultFolderTexture => _folderTexture != null
            ? _folderTexture
            : _folderTexture = EditorGUIUtility.IconContent("Folder Icon")?.image;

        public static Texture FolderTexture(string folderPath)
        {
            if (!IsEmptyFolder(folderPath))
            {
                return DefaultFolderTexture;
            }

            if (_emptyFolderTexture == null)
            {
                _emptyFolderTexture = EditorGUIUtility.IconContent("FolderEmpty Icon")?.image;
            }

            return _emptyFolderTexture != null ? _emptyFolderTexture : DefaultFolderTexture;
        }

        public static Texture GlyphTexture(string glyphName)
        {
            if (string.IsNullOrEmpty(glyphName))
            {
                return null;
            }

            if (GlyphByName.TryGetValue(glyphName, out Texture cached))
            {
                return cached;
            }

            Texture glyph = LoadGlyph(glyphName);
            GlyphByName[glyphName] = glyph;
            return glyph;
        }

        public static Texture AutoGlyph(string folderPath)
        {
            if (AutoGlyphByPath.TryGetValue(folderPath, out Texture cached))
            {
                return cached;
            }

            Texture glyph = GlyphTexture(DominantGlyphName(folderPath));
            AutoGlyphByPath[folderPath] = glyph;
            return glyph;
        }

        public static void InvalidateCache()
        {
            AutoGlyphByPath.Clear();
            EmptyByPath.Clear();
        }

        // Full strength for a direct child, geometrically fading out over deeper descendants.
        public static Color InheritedTint(Color baseColor, int distance)
        {
            const float baseAlpha = 0.22f;
            const float falloffPerLevel = 0.6f;
            return new Color(baseColor.r, baseColor.g, baseColor.b, baseAlpha * Mathf.Pow(falloffPerLevel, distance - 1));
        }

        private static Texture LoadGlyph(string glyphName)
        {
            Type type = GlyphCatalog.FirstOrDefault(entry => entry.Name == glyphName).Type;
            if (type == null)
            {
                return null;
            }

            Texture thumbnail = AssetPreview.GetMiniTypeThumbnail(type);
            return thumbnail != null ? thumbnail : EditorGUIUtility.ObjectContent(null, type).image;
        }

        private static string DominantGlyphName(string folderPath)
        {
            var countByGlyph = new Dictionary<string, int>();
            int total = 0;

            foreach (string assetPath in AssetPathsIn(folderPath))
            {
                Type type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                string glyphName = type == null ? null : CatalogNameFor(type);
                if (glyphName == null)
                {
                    continue;
                }

                countByGlyph.TryGetValue(glyphName, out int count);
                countByGlyph[glyphName] = count + 1;
                total++;
            }

            if (total == 0)
            {
                return null;
            }

            KeyValuePair<string, int> dominant = countByGlyph.OrderByDescending(pair => pair.Value).First();
            return dominant.Value * 2 >= total ? dominant.Key : null;
        }

        private static string CatalogNameFor(Type assetType)
        {
            foreach ((string name, Type type) in GlyphCatalog)
            {
                if (type.IsAssignableFrom(assetType))
                {
                    return name;
                }
            }

            return null;
        }

        private static bool IsEmptyFolder(string folderPath)
        {
            if (EmptyByPath.TryGetValue(folderPath, out bool cached))
            {
                return cached;
            }

            bool isEmpty = !Directory.Exists(folderPath) || !VisibleEntriesIn(folderPath).Any();
            EmptyByPath[folderPath] = isEmpty;
            return isEmpty;
        }

        private static IEnumerable<string> AssetPathsIn(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetFiles(folderPath)
                .Where(IsVisibleAsset)
                .Select(path => path.Replace('\\', '/'));
        }

        private static IEnumerable<string> VisibleEntriesIn(string folderPath)
        {
            return Directory.GetFileSystemEntries(folderPath).Where(IsVisibleAsset);
        }

        private static bool IsVisibleAsset(string path)
        {
            string name = Path.GetFileName(path);
            return !name.StartsWith(".") && !name.EndsWith(".meta");
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString($"#{hex}", out Color color);
            return color;
        }
    }
}
