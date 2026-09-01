using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SoundDataPickerAttribute))]
public class SoundDataPickerPropertyDrawer : PropertyDrawer
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

        if (fieldInfo.FieldType != typeof(SoundData))
        {
            Debug.LogWarning($"[SoundDataPicker] '{fieldInfo.Name}' is not a SoundData field.");
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        Rect fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - Spacing, position.height);
        Rect buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, position.height);

        EditorGUI.PropertyField(fieldRect, property, label);

        if (GUI.Button(buttonRect, "+"))
        {
            var pickerAttribute = (SoundDataPickerAttribute)attribute;
            var targetObject = property.serializedObject.targetObject;
            var propertyPath = property.propertyPath;

            SoundClipPickerWindow.Open(pickerAttribute.Folder, created =>
            {
                // Re-fetched rather than reusing the SerializedProperty captured above - the
                // picker is a separate floating window living across many frames, and the
                // Inspector that opened it (and this SerializedObject) can go stale long before
                // the user finishes searching and ticking clips.
                var refreshedObject = new SerializedObject(targetObject);
                var refreshedProperty = refreshedObject.FindProperty(propertyPath);
                if (refreshedProperty == null)
                    return;

                refreshedProperty.objectReferenceValue = created;
                refreshedObject.ApplyModifiedProperties();
                EditorGUIUtility.PingObject(created);
            });
        }
    }
}
