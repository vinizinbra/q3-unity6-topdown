using System.Linq;
using UnityEditor;

namespace PachaGames.Editor.Folders
{
    internal static class PgFolderSelection
    {
        public static string[] SelectedFolderGuids()
        {
            return Selection.assetGUIDs
                .Where(guid => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid)))
                .ToArray();
        }
    }
}
