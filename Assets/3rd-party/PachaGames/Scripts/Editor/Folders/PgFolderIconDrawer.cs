using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    [InitializeOnLoad]
    internal static class PgFolderIconDrawer
    {
        private const string AutoGlyphsMenuPath = "Tools/Pacha/Folder Styles/Auto Icons From Contents";
        private const string AutoGlyphsPrefKey = "PachaGames.Folders.AutoGlyphs";
        private const float ListRowMaxHeight = 20f;
        private const float GlyphScale = 0.5f;
        private const float GlyphDropShare = 0.08f;

        private static readonly Dictionary<string, bool> IsFolderByGuid = new Dictionary<string, bool>();

        static PgFolderIconDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += DrawFolderItem;
        }

        internal static Vector2 LastProjectClickScreenPos { get; private set; }

        internal static bool AutoGlyphsEnabled
        {
            get => EditorPrefs.GetBool(AutoGlyphsPrefKey, true);
            set => EditorPrefs.SetBool(AutoGlyphsPrefKey, value);
        }

        private static void DrawFolderItem(string guid, Rect rect)
        {
            TrackProjectClick();

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (IsFolder(guid, path))
            {
                PgFolderStyle style = PgFolderStyles.Get(guid);
                Texture glyph = ResolveGlyph(path, style);
                if (style.Color != null || glyph != null)
                {
                    Rect iconRect = IconRectFor(rect);
                    if (style.Color != null)
                    {
                        TintFolder(iconRect, path, style.Color.Value);
                    }

                    if (glyph != null)
                    {
                        DrawGlyph(iconRect, glyph);
                    }
                }
            }

            if (PgFolderStyles.TryGetInheritedColor(path, out Color inheritedColor, out int distance))
            {
                EditorGUI.DrawRect(rect, PgFolderPalette.InheritedTint(inheritedColor, distance));
            }
        }

        private static Texture ResolveGlyph(string path, PgFolderStyle style)
        {
            if (!string.IsNullOrEmpty(style.GlyphName))
            {
                return PgFolderPalette.GlyphTexture(style.GlyphName);
            }

            return AutoGlyphsEnabled ? PgFolderPalette.AutoGlyph(path) : null;
        }

        // Drawing the folder texture over itself replaces exactly its own pixels, so row
        // selection and hover backgrounds survive untouched.
        private static void TintFolder(Rect iconRect, string path, Color color)
        {
            Texture folderTexture = PgFolderPalette.FolderTexture(path);
            if (folderTexture == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(iconRect, folderTexture, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        private static void DrawGlyph(Rect iconRect, Texture glyph)
        {
            float size = iconRect.width * GlyphScale;
            var glyphRect = new Rect(
                iconRect.x + (iconRect.width - size) * 0.5f,
                iconRect.y + (iconRect.height - size) * 0.5f + iconRect.height * GlyphDropShare,
                size,
                size);

            GUI.DrawTexture(glyphRect, glyph, ScaleMode.ScaleToFit);
        }

        private static Rect IconRectFor(Rect rect)
        {
            bool isListRow = rect.height <= ListRowMaxHeight;
            return isListRow
                ? new Rect(rect.x, rect.y, rect.height, rect.height)
                : new Rect(rect.x, rect.y, rect.width, rect.width);
        }

        private static bool IsFolder(string guid, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (IsFolderByGuid.TryGetValue(guid, out bool cached))
            {
                return cached;
            }

            bool isFolder = AssetDatabase.IsValidFolder(path);
            IsFolderByGuid[guid] = isFolder;
            return isFolder;
        }

        private static void TrackProjectClick()
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown || current.type == EventType.ContextClick)
            {
                LastProjectClickScreenPos = GUIUtility.GUIToScreenPoint(current.mousePosition);
            }
        }

        [MenuItem(AutoGlyphsMenuPath)]
        private static void ToggleAutoGlyphs()
        {
            AutoGlyphsEnabled = !AutoGlyphsEnabled;
            EditorApplication.RepaintProjectWindow();
        }

        [MenuItem(AutoGlyphsMenuPath, true)]
        private static bool ValidateToggleAutoGlyphs()
        {
            Menu.SetChecked(AutoGlyphsMenuPath, AutoGlyphsEnabled);
            return true;
        }
    }
}
