using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    internal sealed class PgFavoritesWindow : EditorWindow
    {
        private const float ToolbarHeight = 20f;
        private const float ToolbarPadding = 4f;

        [SerializeField] private TreeViewState _treeViewState;

        private PgFavoritesTreeView _treeView;
        private SearchField _searchField;

        [MenuItem("Window/Pacha/Favorites")]
        private static void Open()
        {
            var window = GetWindow<PgFavoritesWindow>();
            window.titleContent = new GUIContent("Pg Favorites");
            window.Show();
        }

        private void OnEnable()
        {
            _treeViewState ??= new TreeViewState();
            _treeView = new PgFavoritesTreeView(_treeViewState);
            _searchField = new SearchField();
            _searchField.downOrUpArrowKeyPressed += _treeView.SetFocusAndEnsureSelectedItem;

            PgFavorites.Changed += OnFavoritesChanged;
            PgFavorites.AssetsChanged += OnFavoritesChanged;
            Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        private void OnDisable()
        {
            PgFavorites.Changed -= OnFavoritesChanged;
            PgFavorites.AssetsChanged -= OnFavoritesChanged;
            Selection.selectionChanged -= OnGlobalSelectionChanged;
        }

        private void OnGUI()
        {
            var searchRect = new Rect(ToolbarPadding, ToolbarPadding, position.width - ToolbarPadding * 2f, ToolbarHeight);
            _treeView.searchString = _searchField.OnGUI(searchRect, _treeView.searchString);

            var treeRect = new Rect(0f, ToolbarHeight + ToolbarPadding * 2f, position.width,
                position.height - ToolbarHeight - ToolbarPadding * 2f);
            _treeView.OnGUI(treeRect);
        }

        private void OnFavoritesChanged()
        {
            _treeView.Reload();
            Repaint();
        }

        private void OnGlobalSelectionChanged()
        {
            _treeView.SyncExternalSelection();
            Repaint();
        }
    }
}
