using System.Linq;
using UnityEditor;

namespace PachaGames.Editor.Folders
{
    internal static class PgFavoritesMenu
    {
        private const string AddMenuPath = "Assets/Add to Pg Favorites";
        private const string RemoveMenuPath = "Assets/Remove from Pg Favorites";

        [MenuItem(AddMenuPath, false, 30)]
        private static void Add()
        {
            PgFavorites.Add(PgFolderSelection.SelectedFolderGuids());
        }

        [MenuItem(AddMenuPath, true)]
        private static bool ValidateAdd()
        {
            return PgFolderSelection.SelectedFolderGuids().Any(guid => !PgFavorites.IsFavorite(guid));
        }

        [MenuItem(RemoveMenuPath, false, 31)]
        private static void Remove()
        {
            PgFavorites.Remove(PgFolderSelection.SelectedFolderGuids());
        }

        [MenuItem(RemoveMenuPath, true)]
        private static bool ValidateRemove()
        {
            return PgFolderSelection.SelectedFolderGuids().Any(PgFavorites.IsFavorite);
        }
    }
}
