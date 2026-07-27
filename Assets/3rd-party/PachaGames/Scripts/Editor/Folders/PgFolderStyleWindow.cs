using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    internal sealed class PgFolderStyleWindow : EditorWindow
    {
        private const string MenuPath = "Assets/Folder Style...";
        private const float SwatchSize = 28f;
        private const float GlyphSize = 26f;
        private const float Spacing = 3f;
        private const float Padding = 8f;
        private const int ColorsPerRow = 6;
        private const int GlyphsPerRow = 7;

        private static readonly Vector2 WindowSize = new Vector2(224f, 218f);

        private string[] _folderGuids;

        [MenuItem(MenuPath, false, 19)]
        private static void OpenForSelection()
        {
            string[] folderGuids = PgFolderSelection.SelectedFolderGuids();
            if (folderGuids.Length == 0)
            {
                return;
            }

            Open(folderGuids, PgFolderIconDrawer.LastProjectClickScreenPos);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateOpenForSelection()
        {
            return PgFolderSelection.SelectedFolderGuids().Length > 0;
        }

        // Entry point for callers other than the Project window context menu (e.g. Pg Favorites).
        internal static void OpenFor(string[] folderGuids, Vector2 screenPos)
        {
            if (folderGuids.Length > 0)
            {
                Open(folderGuids, screenPos);
            }
        }

        private static void Open(string[] folderGuids, Vector2 screenPos)
        {
            if (screenPos == Vector2.zero)
            {
                screenPos = focusedWindow != null ? focusedWindow.position.center : new Vector2(200f, 200f);
            }

            var window = CreateInstance<PgFolderStyleWindow>();
            window._folderGuids = folderGuids;
            window.ShowAsDropDown(new Rect(screenPos, Vector2.zero), WindowSize);
        }

        private void OnGUI()
        {
            if (_folderGuids == null || _folderGuids.Length == 0)
            {
                Close();
                return;
            }

            PgFolderStyle current = PgFolderStyles.Get(_folderGuids[0]);

            GUILayout.BeginArea(new Rect(Padding, Padding, WindowSize.x - Padding * 2f, WindowSize.y - Padding * 2f));
            GUILayout.Label(HeaderText(), EditorStyles.miniBoldLabel);
            DrawColorGrid(current);
            GUILayout.Space(6f);
            GUILayout.Label("Icon", EditorStyles.miniBoldLabel);
            DrawGlyphGrid(current);
            GUILayout.FlexibleSpace();
            DrawClearButton();
            GUILayout.EndArea();
        }

        private string HeaderText()
        {
            return _folderGuids.Length > 1 ? $"Color ({_folderGuids.Length} folders)" : "Color";
        }

        private void DrawColorGrid(PgFolderStyle current)
        {
            DrawGrid(PgFolderPalette.Colors, ColorsPerRow, SwatchSize, (rect, color) =>
            {
                bool isActive = current.Color != null && current.Color.Value == color;
                if (DrawSlot(rect, isActive))
                {
                    SetStyle(style => style.WithColor(color));
                }

                DrawTintedFolder(rect, color);
            });
        }

        private void DrawGlyphGrid(PgFolderStyle current)
        {
            var glyphNames = new List<string> { null };
            glyphNames.AddRange(PgFolderPalette.GlyphNames);

            DrawGrid(glyphNames, GlyphsPerRow, GlyphSize, (rect, glyphName) =>
            {
                bool isActive = current.GlyphName == glyphName ||
                                (string.IsNullOrEmpty(current.GlyphName) && glyphName == null);
                if (DrawSlot(rect, isActive))
                {
                    SetStyle(style => style.WithGlyph(glyphName));
                }

                DrawGlyph(rect, glyphName);
            });
        }

        private static void DrawGrid<T>(IEnumerable<T> items, int perRow, float slotSize, Action<Rect, T> drawItem)
        {
            int index = 0;
            foreach (T item in items)
            {
                if (index % perRow == 0)
                {
                    if (index > 0)
                    {
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                }

                Rect rect = GUILayoutUtility.GetRect(slotSize, slotSize);
                drawItem(rect, item);
                GUILayout.Space(Spacing);
                index++;
            }

            if (index > 0)
            {
                GUILayout.EndHorizontal();
            }
        }

        private static bool DrawSlot(Rect rect, bool isActive)
        {
            if (isActive)
            {
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.18f));
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static void DrawTintedFolder(Rect rect, Color color)
        {
            Texture folderTexture = PgFolderPalette.DefaultFolderTexture;
            if (folderTexture == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, folderTexture, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        private static void DrawGlyph(Rect rect, string glyphName)
        {
            if (glyphName == null)
            {
                GUI.Label(rect, "—", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Texture glyph = PgFolderPalette.GlyphTexture(glyphName);
            if (glyph != null)
            {
                GUI.DrawTexture(rect, glyph, ScaleMode.ScaleToFit);
            }
        }

        private void DrawClearButton()
        {
            if (GUILayout.Button("Clear", EditorStyles.miniButton))
            {
                SetStyle(_ => PgFolderStyle.None);
            }
        }

        // Writing the style reimports the folder, which must not happen inside the GUI pass.
        private void SetStyle(Func<PgFolderStyle, PgFolderStyle> edit)
        {
            string[] folderGuids = _folderGuids;
            EditorApplication.delayCall += () =>
            {
                PgFolderStyles.Apply(folderGuids, edit);
                if (this != null)
                {
                    Repaint();
                }
            };
        }
    }
}
