using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    internal readonly struct PgFolderStyle
    {
        public static readonly PgFolderStyle None = new PgFolderStyle(null, null);

        public readonly Color? Color;
        public readonly string GlyphName;

        public PgFolderStyle(Color? color, string glyphName)
        {
            Color = color;
            GlyphName = glyphName;
        }

        public bool IsEmpty => Color == null && string.IsNullOrEmpty(GlyphName);

        public PgFolderStyle WithColor(Color? color) => new PgFolderStyle(color, GlyphName);

        public PgFolderStyle WithGlyph(string glyphName) => new PgFolderStyle(Color, glyphName);
    }

    internal static class PgFolderStyles
    {
        private static readonly Dictionary<string, PgFolderStyle> StyleByGuid = new Dictionary<string, PgFolderStyle>();

        public static PgFolderStyle Get(string guid)
        {
            if (StyleByGuid.TryGetValue(guid, out PgFolderStyle cached))
            {
                return cached;
            }

            PgFolderStyle style = ReadFromMeta(AssetDatabase.GUIDToAssetPath(guid));
            StyleByGuid[guid] = style;
            return style;
        }

        public static void Apply(IEnumerable<string> guids, Func<PgFolderStyle, PgFolderStyle> edit)
        {
            foreach (string guid in guids)
            {
                WriteToMeta(AssetDatabase.GUIDToAssetPath(guid), edit(Get(guid)));
            }

            InvalidateCache();
            EditorApplication.RepaintProjectWindow();
        }

        public static void InvalidateCache()
        {
            StyleByGuid.Clear();
            PgFolderPalette.InvalidateCache();
        }

        // Walks up from itemPath's containing folder looking for the nearest colored ancestor,
        // so a colored folder's contents can be gradient-tinted by how deep they are inside it.
        public const int MaxInheritedColorDistance = 4;

        public static bool TryGetInheritedColor(string itemPath, out Color color, out int distance)
        {
            string current = Path.GetDirectoryName(itemPath)?.Replace('\\', '/');

            for (distance = 1; !string.IsNullOrEmpty(current) && distance <= MaxInheritedColorDistance; distance++)
            {
                PgFolderStyle style = Get(AssetDatabase.AssetPathToGUID(current));
                if (style.Color != null)
                {
                    color = style.Color.Value;
                    return true;
                }

                current = Path.GetDirectoryName(current)?.Replace('\\', '/');
            }

            color = default;
            distance = 0;
            return false;
        }

        private static PgFolderStyle ReadFromMeta(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return PgFolderStyle.None;
            }

            AssetImporter importer = AssetImporter.GetAtPath(folderPath);
            if (importer == null || string.IsNullOrEmpty(importer.userData))
            {
                return PgFolderStyle.None;
            }

            MetaPayload payload = ParsePayload(importer.userData);
            if (payload == null)
            {
                return PgFolderStyle.None;
            }

            return new PgFolderStyle(ParseColor(payload.pgFolderColor), payload.pgFolderGlyph);
        }

        private static void WriteToMeta(string folderPath, PgFolderStyle style)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            AssetImporter importer = AssetImporter.GetAtPath(folderPath);
            if (importer == null)
            {
                return;
            }

            string userData = style.IsEmpty ? string.Empty : JsonUtility.ToJson(ToPayload(style));
            if (importer.userData == userData)
            {
                return;
            }

            importer.userData = userData;
            importer.SaveAndReimport();
        }

        private static MetaPayload ToPayload(PgFolderStyle style)
        {
            return new MetaPayload
            {
                pgFolderColor = style.Color == null ? string.Empty : ColorUtility.ToHtmlStringRGB(style.Color.Value),
                pgFolderGlyph = style.GlyphName ?? string.Empty
            };
        }

        // userData is a free-form string shared with any other tool that claims the folder's importer.
        private static MetaPayload ParsePayload(string userData)
        {
            try
            {
                return JsonUtility.FromJson<MetaPayload>(userData);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return null;
            }

            return ColorUtility.TryParseHtmlString($"#{hex}", out Color color) ? color : (Color?)null;
        }

        [Serializable]
        private class MetaPayload
        {
            public string pgFolderColor;
            public string pgFolderGlyph;
        }
    }

    internal class PgFolderStyleWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            PgFolderStyles.InvalidateCache();
        }
    }
}
