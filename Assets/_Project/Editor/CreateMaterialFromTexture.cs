using UnityEditor;
using UnityEngine;

public static class CreateMaterialFromTexture
{
    private const string TargetFolder = "Assets/_Project/Art/Material";

    [MenuItem("Assets/Create Material", false, 20)]
    private static void CreateMaterial()
    {
        EnsureFolderExists(TargetFolder);

        foreach (Texture2D texture in Selection.GetFiltered<Texture2D>(SelectionMode.Assets))
        {
            Material material = new Material(GetDefaultShader()) { mainTexture = texture };

            string path = AssetDatabase.GenerateUniqueAssetPath($"{TargetFolder}/{texture.name}.mat");
            AssetDatabase.CreateAsset(material, path);

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
        }

        AssetDatabase.SaveAssets();
    }

    [MenuItem("Assets/Create Material", true)]
    private static bool ValidateCreateMaterial()
    {
        return Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;
    }

    private static Shader GetDefaultShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
    }

    private static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
