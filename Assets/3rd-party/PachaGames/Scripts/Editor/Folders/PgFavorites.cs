using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace PachaGames.Editor.Folders
{
    // Personal to this machine (EditorPrefs, keyed by product GUID), not shared via git.
    internal static class PgFavorites
    {
        private static readonly string PrefKey = $"PachaGames.Favorites.{PlayerSettings.productGUID}";

        private static List<string> _guids;

        public static event Action Changed;

        // Fires on any asset import/move/delete, so a favorited folder's tree stays in sync.
        public static event Action AssetsChanged;

        public static IReadOnlyList<string> Guids => EnsureLoaded();

        public static bool IsFavorite(string guid)
        {
            return EnsureLoaded().Contains(guid);
        }

        public static void Add(IEnumerable<string> guids)
        {
            List<string> list = EnsureLoaded();
            bool changed = false;
            foreach (string guid in guids)
            {
                if (!list.Contains(guid))
                {
                    list.Add(guid);
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
        }

        public static void Remove(IEnumerable<string> guids)
        {
            var toRemove = new HashSet<string>(guids);
            if (EnsureLoaded().RemoveAll(guid => toRemove.Contains(guid)) > 0)
            {
                Save();
            }
        }

        public static void Prune()
        {
            if (EnsureLoaded().RemoveAll(guid => !AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid))) > 0)
            {
                Save();
            }
        }

        private static List<string> EnsureLoaded()
        {
            if (_guids != null)
            {
                return _guids;
            }

            string raw = EditorPrefs.GetString(PrefKey, string.Empty);
            _guids = string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw.Split(';').Where(guid => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid))).ToList();
            return _guids;
        }

        private static void Save()
        {
            EditorPrefs.SetString(PrefKey, string.Join(";", _guids));
            Changed?.Invoke();
        }

        internal static void RaiseAssetsChanged()
        {
            AssetsChanged?.Invoke();
        }
    }

    internal class PgFavoritesWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (deleted.Length > 0)
            {
                PgFavorites.Prune();
            }

            // OnPostprocessAllAssets fires for every asset change project-wide; only reload the
            // (disk-walking) favorites tree when a changed path is actually inside a favorited folder.
            if (IsRelevant(imported) || IsRelevant(deleted) || IsRelevant(moved) || IsRelevant(movedFrom))
            {
                PgFavorites.RaiseAssetsChanged();
            }
        }

        private static bool IsRelevant(string[] paths)
        {
            if (paths.Length == 0)
            {
                return false;
            }

            foreach (string guid in PgFavorites.Guids)
            {
                string root = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                foreach (string path in paths)
                {
                    if (path == root || path.StartsWith(root + "/", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
