using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// IMGUI pagination: draws only the rows of the current page, so a list of thousands of entries costs
    /// the same per repaint as a list of fifty. Page resets automatically when a different list instance
    /// (or a list of a different size) is handed in, which is what happens when a filter changes.
    /// </summary>
    internal sealed class PagedListDrawer
    {
        private static readonly int[] PageSizes = { 25, 50, 100, 250 };
        private static readonly string[] PageSizeLabels = { "25", "50", "100", "250" };

        public int PageSize = 50;
        public int PageIndex;

        private object _lastSource;
        private int _lastCount = -1;

        public void Reset() => PageIndex = 0;

        public int PageCount(int itemCount) => Math.Max(1, (itemCount + PageSize - 1) / PageSize);

        public void Draw<T>(IList<T> items, Action<T, int> drawRow, Action drawHeader = null)
        {
            if (!ReferenceEquals(items, _lastSource) || items.Count != _lastCount)
            {
                _lastSource = items;
                _lastCount = items.Count;
                PageIndex = 0;
            }

            int pages = PageCount(items.Count);
            PageIndex = Mathf.Clamp(PageIndex, 0, pages - 1);

            int start = PageIndex * PageSize;
            int end = Math.Min(items.Count, start + PageSize);

            DrawFooter(items.Count, pages, start, end);
            drawHeader?.Invoke();

            if (items.Count == 0)
            {
                EditorGUILayout.LabelField("Nothing to show.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (int i = start; i < end; i++)
                drawRow(items[i], i);

            if (end - start > 15)
                DrawFooter(items.Count, pages, start, end);
        }

        private void DrawFooter(int count, int pages, int start, int end)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(count == 0 ? "0 rows" : $"Rows {start + 1}–{end} of {count}", EditorStyles.miniLabel, GUILayout.Width(150f));
                GUILayout.FlexibleSpace();

                GUILayout.Label("Page size", EditorStyles.miniLabel);
                int sizeIndex = Array.IndexOf(PageSizes, PageSize);
                if (sizeIndex < 0) sizeIndex = 1;
                int newSizeIndex = EditorGUILayout.Popup(sizeIndex, PageSizeLabels, EditorStyles.toolbarPopup, GUILayout.Width(50f));
                if (newSizeIndex != sizeIndex)
                {
                    int firstRow = PageIndex * PageSize;
                    PageSize = PageSizes[newSizeIndex];
                    PageIndex = firstRow / PageSize;
                }

                GUILayout.Space(8f);

                using (new EditorGUI.DisabledScope(PageIndex <= 0))
                {
                    if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(28f))) PageIndex = 0;
                    if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(28f))) PageIndex--;
                }

                GUILayout.Label("Page", EditorStyles.miniLabel);
                int typed = EditorGUILayout.IntField(PageIndex + 1, EditorStyles.toolbarTextField, GUILayout.Width(40f));
                if (typed - 1 != PageIndex)
                    PageIndex = Mathf.Clamp(typed - 1, 0, pages - 1);
                GUILayout.Label($"/ {pages}", EditorStyles.miniLabel, GUILayout.Width(40f));

                using (new EditorGUI.DisabledScope(PageIndex >= pages - 1))
                {
                    if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(28f))) PageIndex++;
                    if (GUILayout.Button("⏭", EditorStyles.toolbarButton, GUILayout.Width(28f))) PageIndex = pages - 1;
                }
            }
        }
    }
}
