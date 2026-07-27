using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PachaGames.Editor.Folders
{
    // Mirrors the Project window's One Column Layout: every favorited folder is a root,
    // expanded to show its full nested subtree of folders and files, with the same
    // create/rename/duplicate/delete/move actions available in the real Project window.
    internal sealed class PgFavoritesTreeView : TreeView
    {
        private readonly Dictionary<string, int> _idByPath = new Dictionary<string, int>();
        private readonly Dictionary<int, string> _pathById = new Dictionary<int, string>();
        private readonly Dictionary<int, Object> _subAssetById = new Dictionary<int, Object>();
        private readonly HashSet<string> _favoriteRootPaths = new HashSet<string>();
        private int _nextId;

        // Set right after "Create > C# Script" so the first RenameEnded also patches the class name,
        // matching how the real Project window bakes the typed name into a brand new script.
        private int? _pendingNewScriptId;
        private string _pendingNewScriptClassName;

        public PgFavoritesTreeView(TreeViewState state) : base(state)
        {
            showAlternatingRowBackgrounds = true;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = -1, depth = -1, displayName = "Root" };
            var items = new List<TreeViewItem>();
            _favoriteRootPaths.Clear();

            foreach (string guid in PgFavorites.Guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                _favoriteRootPaths.Add(path);
                AddRecursive(path, 0, items);
            }

            if (items.Count == 0)
            {
                items.Add(new TreeViewItem(IdFor("<empty>"), 0,
                    "Drag a folder here, or right-click a folder in Project > Add to Pg Favorites"));
            }

            SetupParentsAndChildren(root, items);
            return root;
        }

        // TreeViewUtility<TIdentifier>.SetupParentsAndChildrenFromDepths is internal in this Unity
        // version, so link items up from their depths ourselves.
        private static void SetupParentsAndChildren(TreeViewItem root, List<TreeViewItem> items)
        {
            root.children = new List<TreeViewItem>();
            if (items.Count == 0)
            {
                return;
            }

            var ancestors = new Stack<TreeViewItem>();
            ancestors.Push(root);

            foreach (TreeViewItem item in items)
            {
                while (ancestors.Peek().depth >= item.depth)
                {
                    ancestors.Pop();
                }

                ancestors.Peek().AddChild(item);
                ancestors.Push(item);
            }
        }

        // Matches on name, keeping ancestor folders visible so a hit stays reachable in the tree,
        // rather than replacing the view with a flat result list like the Project window's search does.
        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            if (string.IsNullOrEmpty(searchString))
            {
                return base.BuildRows(root);
            }

            var rows = new List<TreeViewItem>();
            foreach (TreeViewItem child in root.children ?? Enumerable.Empty<TreeViewItem>())
            {
                AddMatchingRows(child, rows);
            }

            return rows;
        }

        private bool AddMatchingRows(TreeViewItem item, List<TreeViewItem> rows)
        {
            bool selfMatches = item.displayName.IndexOf(searchString, System.StringComparison.OrdinalIgnoreCase) >= 0;

            var childRows = new List<TreeViewItem>();
            if (item.children != null)
            {
                foreach (TreeViewItem child in item.children)
                {
                    AddMatchingRows(child, childRows);
                }
            }

            if (!selfMatches && childRows.Count == 0)
            {
                return false;
            }

            rows.Add(item);
            rows.AddRange(childRows);
            return true;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            // Drawn before base.RowGUI so the wash sits behind the icon/label instead of over them.
            if (Event.current.type == EventType.Repaint)
            {
                string gradientPath = GradientPathFor(args.item.id);
                if (!string.IsNullOrEmpty(gradientPath) &&
                    PgFolderStyles.TryGetInheritedColor(gradientPath, out Color inheritedColor, out int distance))
                {
                    EditorGUI.DrawRect(args.rowRect, PgFolderPalette.InheritedTint(inheritedColor, distance));
                }
            }

            base.RowGUI(args);

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (_subAssetById.TryGetValue(args.item.id, out Object subAsset))
            {
                DrawSubAssetPreview(args, subAsset);
                return;
            }

            if (!_pathById.TryGetValue(args.item.id, out string path) || !AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            PgFolderStyle style = PgFolderStyles.Get(AssetDatabase.AssetPathToGUID(path));
            Texture folderTexture = style.Color != null ? PgFolderPalette.FolderTexture(path) : null;
            if (folderTexture == null)
            {
                return;
            }

            float indent = GetContentIndent(args.item);
            var iconRect = new Rect(args.rowRect.x + indent, args.rowRect.y, args.rowRect.height, args.rowRect.height);

            Color previous = GUI.color;
            GUI.color = style.Color.Value;
            GUI.DrawTexture(iconRect, folderTexture, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        // AssetPreview generates the real cropped-sprite thumbnail asynchronously; until it's
        // ready this falls back to the mini-thumbnail already baked into item.icon, and
        // self-corrects on a later repaint once the preview finishes loading.
        private void DrawSubAssetPreview(RowGUIArgs args, Object subAsset)
        {
            Texture2D preview = AssetPreview.GetAssetPreview(subAsset);
            if (preview == null)
            {
                return;
            }

            float indent = GetContentIndent(args.item);
            var iconRect = new Rect(args.rowRect.x + indent, args.rowRect.y, args.rowRect.height, args.rowRect.height);
            GUI.DrawTexture(iconRect, preview, ScaleMode.ScaleToFit);
        }

        protected override void DoubleClickedItem(int id)
        {
            if (!_pathById.TryGetValue(id, out string path))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                SetExpanded(id, !IsExpanded(id));
                return;
            }

            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(path));
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            Object[] selected = selectedIds
                .Select(ResolveObject)
                .Where(obj => obj != null)
                .ToArray();

            if (selected.Length == 0)
            {
                return;
            }

            Selection.objects = selected;
            EditorGUIUtility.PingObject(selected[0]);
        }

        // Called whenever the global Unity selection changes (Project window, Hierarchy, code, ...).
        // If the selected asset lives inside a favorited folder, reveal it here too; otherwise
        // leave this tree alone (the Inspector already follows Selection regardless).
        public void SyncExternalSelection()
        {
            Object activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(activeObject);
            if (string.IsNullOrEmpty(path) || !_idByPath.TryGetValue(path, out int id) || FindItem(id, rootItem) == null)
            {
                return;
            }

            IList<int> currentSelection = GetSelection();
            if (currentSelection.Count == 1 && currentSelection[0] == id)
            {
                return;
            }

            SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
        }

        // Sprite children aren't in _pathById (they're sub-assets, not files), so fall back to
        // their parent texture's path - same gradient distance as the texture row itself.
        private string GradientPathFor(int id)
        {
            if (_pathById.TryGetValue(id, out string path))
            {
                return path;
            }

            return _subAssetById.TryGetValue(id, out Object subAsset) ? AssetDatabase.GetAssetPath(subAsset) : null;
        }

        private Object ResolveObject(int id)
        {
            if (_subAssetById.TryGetValue(id, out Object subAsset))
            {
                return subAsset;
            }

            return _pathById.TryGetValue(id, out string path) ? AssetDatabase.LoadAssetAtPath<Object>(path) : null;
        }

        protected override void ContextClickedItem(int id)
        {
            if (!_pathById.TryGetValue(id, out string path) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                return;
            }

            bool isFolder = AssetDatabase.IsValidFolder(path);
            string targetFolder = isFolder ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
            Vector2 menuScreenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create/Folder"), false, () => CreateFolder(targetFolder));
            menu.AddItem(new GUIContent("Create/C# Script"), false, () => CreateScript(targetFolder));
            menu.AddSeparator(string.Empty);

            string[] selectedFolderGuids = SelectedFolderGuids();
            if (selectedFolderGuids.Length > 0)
            {
                menu.AddItem(new GUIContent("Folder Style..."), false,
                    () => PgFolderStyleWindow.OpenFor(selectedFolderGuids, menuScreenPos));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Rename"), false, () => BeginRename(FindItem(id, rootItem)));
            menu.AddItem(new GUIContent("Duplicate"), false, DuplicateSelected);
            menu.AddItem(new GUIContent("Delete"), false, DeleteSelected);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Copy Path"), false, () => CopyPath(path));
            menu.AddItem(new GUIContent("Show in Explorer"), false, () => EditorUtility.RevealInFinder(path));

            if (_favoriteRootPaths.Contains(path))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Remove from Favorites"), false,
                    () => PgFavorites.Remove(new[] { AssetDatabase.AssetPathToGUID(path) }));
            }

            menu.ShowAsContext();
        }

        protected override void KeyEvent()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown)
            {
                return;
            }

            if (current.keyCode == KeyCode.F2 && GetSelection().Count == 1)
            {
                TreeViewItem item = FindItem(GetSelection()[0], rootItem);
                if (item != null && CanRename(item))
                {
                    BeginRename(item);
                    current.Use();
                }
            }
            else if ((current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace) && GetSelection().Count > 0)
            {
                DeleteSelected();
                current.Use();
            }
            else if (current.keyCode == KeyCode.D && (current.control || current.command) && GetSelection().Count > 0)
            {
                DuplicateSelected();
                current.Use();
            }
        }

        protected override bool CanRename(TreeViewItem item)
        {
            return _pathById.TryGetValue(item.id, out string path) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            int? pendingScriptId = _pendingNewScriptId;
            string pendingScriptClassName = _pendingNewScriptClassName;
            _pendingNewScriptId = null;
            _pendingNewScriptClassName = null;

            if (!args.acceptedRename || args.newName == args.originalName || !_pathById.TryGetValue(args.itemID, out string oldPath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(oldPath)?.Replace('\\', '/');
            string extension = AssetDatabase.IsValidFolder(oldPath) ? string.Empty : Path.GetExtension(oldPath);
            string newBaseName = extension.Length > 0 && args.newName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase)
                ? args.newName.Substring(0, args.newName.Length - extension.Length)
                : args.newName;
            string newPath = $"{directory}/{newBaseName}{extension}";

            EditorApplication.delayCall += () =>
            {
                string error = AssetDatabase.RenameAsset(oldPath, newBaseName);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"Pg Favorites: rename failed: {error}");
                    return;
                }

                if (args.itemID == pendingScriptId && !string.IsNullOrEmpty(pendingScriptClassName))
                {
                    PatchScriptClassName(newPath, pendingScriptClassName, SanitizeIdentifier(newBaseName));
                }
            };
        }

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            return true;
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            Object[] dragged = args.draggedItemIDs
                .Select(ResolveObject)
                .Where(obj => obj != null)
                .ToArray();

            if (dragged.Length == 0)
            {
                return;
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = dragged;
            DragAndDrop.paths = dragged.Select(AssetDatabase.GetAssetPath).ToArray();
            DragAndDrop.StartDrag(dragged.Length > 1 ? "<Multiple>" : dragged[0].name);
        }

        // Handles reordering within this tree, drops coming from the real Project window, and
        // raw OS file drops (Finder/Explorer): dropping onto a folder row moves/imports the
        // dragged assets into it; dropping on empty space (or the placeholder row) instead
        // favorites any dragged folders at the top level (external files need an explicit
        // folder row target, since there's no single "active folder" across favorite roots).
        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            Object[] draggedObjects = DragAndDrop.objectReferences;
            string destFolder = DestinationFolderFor(args);

            if (draggedObjects.Length == 0)
            {
                string[] externalPaths = DragAndDrop.paths;
                if (externalPaths.Length == 0 || string.IsNullOrEmpty(destFolder))
                {
                    return DragAndDropVisualMode.None;
                }

                if (args.performDrop)
                {
                    string[] pathsToImport = externalPaths.ToArray();
                    EditorApplication.delayCall += () => ImportExternalPaths(pathsToImport, destFolder);
                }

                return DragAndDropVisualMode.Copy;
            }

            if (string.IsNullOrEmpty(destFolder))
            {
                string[] folderGuids = draggedObjects
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(AssetDatabase.IsValidFolder)
                    .Select(AssetDatabase.AssetPathToGUID)
                    .ToArray();

                if (folderGuids.Length == 0)
                {
                    return DragAndDropVisualMode.None;
                }

                if (args.performDrop)
                {
                    PgFavorites.Add(folderGuids);
                }

                return DragAndDropVisualMode.Copy;
            }

            if (args.performDrop)
            {
                string[] sourcePaths = draggedObjects.Select(AssetDatabase.GetAssetPath).ToArray();
                EditorApplication.delayCall += () =>
                {
                    foreach (string sourcePath in sourcePaths)
                    {
                        MoveAssetInto(sourcePath, destFolder);
                    }
                };
            }

            return DragAndDropVisualMode.Move;
        }

        private string DestinationFolderFor(DragAndDropArgs args)
        {
            TreeViewItem targetItem = args.parentItem;
            if (targetItem == null || targetItem.depth < 0 || !_pathById.TryGetValue(targetItem.id, out string path))
            {
                return null;
            }

            return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
        }

        private static void ImportExternalPaths(IEnumerable<string> absolutePaths, string destFolder)
        {
            foreach (string absolutePath in absolutePaths)
            {
                string destPath = AssetDatabase.GenerateUniqueAssetPath($"{destFolder}/{Path.GetFileName(absolutePath)}");
                try
                {
                    if (Directory.Exists(absolutePath))
                    {
                        CopyDirectoryRecursive(absolutePath, destPath);
                    }
                    else if (File.Exists(absolutePath))
                    {
                        File.Copy(absolutePath, destPath);
                    }
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"Pg Favorites: couldn't import '{absolutePath}': {exception.Message}");
                }
            }

            AssetDatabase.Refresh();
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                File.Copy(filePath, $"{destDir}/{Path.GetFileName(filePath)}");
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(subDir, $"{destDir}/{Path.GetFileName(subDir)}");
            }
        }

        private static void MoveAssetInto(string sourcePath, string destFolder)
        {
            string currentFolder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(sourcePath) || currentFolder == destFolder)
            {
                return;
            }

            string destPath = AssetDatabase.GenerateUniqueAssetPath($"{destFolder}/{Path.GetFileName(sourcePath)}");
            string error = AssetDatabase.MoveAsset(sourcePath, destPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"Pg Favorites: couldn't move '{sourcePath}' to '{destFolder}': {error}");
            }
        }

        private void CreateFolder(string targetFolder)
        {
            EditorApplication.delayCall += () =>
            {
                string uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/New Folder");
                string guid = AssetDatabase.CreateFolder(targetFolder, Path.GetFileName(uniquePath));
                RevealAndRename(AssetDatabase.GUIDToAssetPath(guid));
            };
        }

        private void CreateScript(string targetFolder)
        {
            EditorApplication.delayCall += () =>
            {
                string template = ReadScriptTemplate();
                if (template == null)
                {
                    Debug.LogWarning("Pg Favorites: couldn't find the MonoBehaviour script template.");
                    return;
                }

                string uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/NewMonoBehaviourScript.cs");
                string scriptName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(uniquePath));
                File.WriteAllText(uniquePath, ApplyScriptTemplate(template, scriptName));
                AssetDatabase.ImportAsset(uniquePath);

                _pendingNewScriptId = IdFor(uniquePath);
                _pendingNewScriptClassName = scriptName;
                RevealAndRename(uniquePath);
            };
        }

        private void RevealAndRename(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Reload();
            int id = IdFor(path);
            TreeViewItem item = FindItem(id, rootItem);
            if (item == null)
            {
                return;
            }

            SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame | TreeViewSelectionOptions.FireSelectionChanged);
            BeginRename(item);
        }

        private void DuplicateSelected()
        {
            string[] paths = SelectedPaths();
            if (paths.Length == 0)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                foreach (string path in paths)
                {
                    AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(path));
                }
            };
        }

        private void DeleteSelected()
        {
            string[] paths = SelectedPaths();
            if (paths.Length == 0)
            {
                return;
            }

            string message = paths.Length == 1
                ? $"Delete '{Path.GetFileName(paths[0])}'?\n\nYou cannot undo this action."
                : $"Delete {paths.Length} selected items?\n\nYou cannot undo this action.";

            if (!EditorUtility.DisplayDialog("Delete Assets", message, "Delete", "Cancel"))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                foreach (string path in paths)
                {
                    AssetDatabase.MoveAssetToTrash(path);
                }
            };
        }

        private string[] SelectedPaths()
        {
            return GetSelection()
                .Select(id => _pathById.TryGetValue(id, out string path) ? path : null)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToArray();
        }

        private string[] SelectedFolderGuids()
        {
            return SelectedPaths()
                .Where(AssetDatabase.IsValidFolder)
                .Select(AssetDatabase.AssetPathToGUID)
                .ToArray();
        }

        private static void CopyPath(string path)
        {
            GUIUtility.systemCopyBuffer = Path.GetFullPath(path).Replace('\\', '/');
        }

        private static string ReadScriptTemplate()
        {
            string templatesDir = Path.Combine(EditorApplication.applicationContentsPath, "Resources/ScriptTemplates");
            if (!Directory.Exists(templatesDir))
            {
                return null;
            }

            string templatePath = Directory.GetFiles(templatesDir, "*NewMonoBehaviourScript.cs.txt").FirstOrDefault();
            return templatePath != null ? File.ReadAllText(templatePath) : null;
        }

        private static string ApplyScriptTemplate(string template, string scriptName)
        {
            string result = template.Replace("#SCRIPTNAME#", scriptName).Replace("#NOTRIM#", string.Empty);

            string rootNamespace = EditorSettings.projectGenerationRootNamespace;
            result = string.IsNullOrEmpty(rootNamespace)
                ? RemoveLine(RemoveLine(result, "#ROOTNAMESPACEBEGIN#"), "#ROOTNAMESPACEEND#")
                : result.Replace("#ROOTNAMESPACEBEGIN#", $"namespace {rootNamespace}\n{{").Replace("#ROOTNAMESPACEEND#", "}");

            return result;
        }

        private static string RemoveLine(string text, string marker)
        {
            return string.Join("\n", text.Split('\n').Where(line => !line.Contains(marker)));
        }

        private static void PatchScriptClassName(string scriptPath, string oldClassName, string newClassName)
        {
            if (oldClassName == newClassName || !File.Exists(scriptPath))
            {
                return;
            }

            string content = File.ReadAllText(scriptPath);
            string patched = System.Text.RegularExpressions.Regex.Replace(
                content, $@"\bclass\s+{System.Text.RegularExpressions.Regex.Escape(oldClassName)}\b", $"class {newClassName}");

            if (patched != content)
            {
                File.WriteAllText(scriptPath, patched);
                AssetDatabase.ImportAsset(scriptPath);
            }
        }

        private static string SanitizeIdentifier(string name)
        {
            string result = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            return result.Length == 0 || char.IsDigit(result[0]) ? "_" + result : result;
        }

        private void AddRecursive(string path, int depth, List<TreeViewItem> items)
        {
            var item = new TreeViewItem(IdFor(path), depth, Path.GetFileName(path))
            {
                icon = AssetDatabase.GetCachedIcon(path) as Texture2D
            };
            items.Add(item);

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string childPath in ChildPaths(path))
                {
                    AddRecursive(childPath, depth + 1, items);
                }

                return;
            }

            AddSpriteChildren(path, depth + 1, items);
        }

        // Sprite Mode = Multiple textures expose each slice as a sub-asset representation;
        // the real Project window shows these as an expandable child list under the texture.
        private void AddSpriteChildren(string path, int depth, List<TreeViewItem> items)
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            if (subAssets.Length <= 1)
            {
                return;
            }

            foreach (Object subAsset in subAssets)
            {
                if (subAsset is not Sprite sprite)
                {
                    continue;
                }

                items.Add(new TreeViewItem(IdForSubAsset(path, sprite.name, sprite), depth, sprite.name)
                {
                    icon = AssetPreview.GetMiniThumbnail(sprite)
                });
            }
        }

        private int IdForSubAsset(string parentPath, string name, Object asset)
        {
            string key = $"{parentPath}::{name}";
            if (_idByPath.TryGetValue(key, out int id))
            {
                _subAssetById[id] = asset;
                return id;
            }

            id = _nextId++;
            _idByPath[key] = id;
            _subAssetById[id] = asset;
            return id;
        }

        private static IEnumerable<string> ChildPaths(string folderPath)
        {
            return Directory.GetFileSystemEntries(folderPath)
                .Where(IsVisible)
                .Select(path => path.Replace('\\', '/'))
                .OrderByDescending(AssetDatabase.IsValidFolder)
                .ThenBy(Path.GetFileName, System.StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsVisible(string path)
        {
            string name = Path.GetFileName(path);
            return !name.StartsWith(".") && !name.EndsWith(".meta");
        }

        private int IdFor(string path)
        {
            if (_idByPath.TryGetValue(path, out int id))
            {
                _pathById[id] = path;
                return id;
            }

            id = _nextId++;
            _idByPath[path] = id;
            _pathById[id] = path;
            return id;
        }
    }
}
