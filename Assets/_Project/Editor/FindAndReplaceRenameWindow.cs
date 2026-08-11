using System;
using UnityEditor;
using UnityEngine;

public class FindAndReplaceRenameWindow : EditorWindow
{
    private UnityEngine.Object[] targets;
    private string find = "";
    private string replace = "";
    private bool caseSensitive = true;

    [MenuItem("Assets/Find and Replace Rename...", false, 20)]
    private static void Open()
    {
        FindAndReplaceRenameWindow window = GetWindow<FindAndReplaceRenameWindow>(true, "Find and Replace Rename");
        window.targets = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        window.minSize = new Vector2(360, 140);
        window.ShowUtility();
    }

    [MenuItem("Assets/Find and Replace Rename...", true)]
    private static bool ValidateOpen()
    {
        return Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets).Length > 0;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField($"{targets.Length} object(s) selected", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        find = EditorGUILayout.TextField("Find", find);
        replace = EditorGUILayout.TextField("Replace", replace);
        caseSensitive = EditorGUILayout.Toggle("Case Sensitive", caseSensitive);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(find)))
        {
            if (GUILayout.Button("Rename"))
            {
                Rename();
                Close();
            }
        }
    }

    private void Rename()
    {
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (UnityEngine.Object target in targets)
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string newName = ReplaceIgnoreCase(target.name, find, replace, comparison);
                if (newName == target.name)
                {
                    continue;
                }

                AssetDatabase.RenameAsset(path, newName);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
    }

    private static string ReplaceIgnoreCase(string source, string oldValue, string newValue, StringComparison comparison)
    {
        if (comparison == StringComparison.Ordinal)
        {
            return source.Replace(oldValue, newValue);
        }

        System.Text.StringBuilder result = new System.Text.StringBuilder();
        int index = 0;
        while (index < source.Length)
        {
            int matchIndex = source.IndexOf(oldValue, index, comparison);
            if (matchIndex < 0)
            {
                result.Append(source, index, source.Length - index);
                break;
            }

            result.Append(source, index, matchIndex - index);
            result.Append(newValue);
            index = matchIndex + oldValue.Length;
        }

        return result.ToString();
    }
}
