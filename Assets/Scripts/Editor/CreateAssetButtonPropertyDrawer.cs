using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(CreateAssetButtonAttribute))]
public class CreateAssetButtonPropertyDrawer : PropertyDrawer
{
    private const float ButtonWidth = 24f;
    private const float Spacing = 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        Rect fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - Spacing, position.height);
        Rect buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, position.height);

        EditorGUI.PropertyField(fieldRect, property, label);

        if (GUI.Button(buttonRect, "+"))
        {
            CreateAndAssignAsset(property);
        }
    }

    private void CreateAndAssignAsset(SerializedProperty property)
    {
        var fieldType = fieldInfo.FieldType;
        if (typeof(ScriptableObject).IsAssignableFrom(fieldType) == false)
        {
            Debug.LogWarning($"[CreateAssetButton] '{fieldInfo.Name}' is not a ScriptableObject field.");
            return;
        }

        if (fieldType.IsAbstract)
        {
            Debug.LogWarning($"[CreateAssetButton] '{fieldType.Name}' is abstract - assign a concrete subtype's field instead.");
            return;
        }

        var createAssetButton = (CreateAssetButtonAttribute)attribute;
        string folder = string.IsNullOrEmpty(createAssetButton.Folder)
            ? $"Assets/QuantumUser/Resources/{fieldType.Name}"
            : createAssetButton.Folder;

        EnsureFolderExists(folder);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New {fieldType.Name}.asset");
        var instance = ScriptableObject.CreateInstance(fieldType);
        AssetDatabase.CreateAsset(instance, path);
        AssetDatabase.SaveAssets();

        property.objectReferenceValue = instance;
        property.serializedObject.ApplyModifiedProperties();

        EditorGUIUtility.PingObject(instance);
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
