using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Transform))]
[CanEditMultipleObjects]
public class TransformResetButtonsEditor : Editor
{
    private const float ButtonWidth = 20f;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawVectorFieldWithReset("Position", serializedObject.FindProperty("m_LocalPosition"), Vector3.zero, "Reset Position");
        DrawRotationFieldWithReset();
        DrawVectorFieldWithReset("Scale", serializedObject.FindProperty("m_LocalScale"), Vector3.one, "Reset Scale");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawVectorFieldWithReset(string label, SerializedProperty property, Vector3 resetValue, string undoName)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(property, new GUIContent(label));
        if (GUILayout.Button("R", GUILayout.Width(ButtonWidth)))
        {
            property.vector3Value = resetValue;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRotationFieldWithReset()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUI.showMixedValue = HasMixedRotation();
        EditorGUI.BeginChangeCheck();
        Vector3 eulerAngles = EditorGUILayout.Vector3Field("Rotation", ((Transform)target).localEulerAngles);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObjects(targets, "Rotate Transform");
            foreach (Transform t in targets)
            {
                t.localEulerAngles = eulerAngles;
            }
        }
        EditorGUI.showMixedValue = false;

        if (GUILayout.Button("R", GUILayout.Width(ButtonWidth)))
        {
            Undo.RecordObjects(targets, "Reset Rotation");
            foreach (Transform t in targets)
            {
                t.localEulerAngles = Vector3.zero;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool HasMixedRotation()
    {
        if (targets.Length < 2)
        {
            return false;
        }

        Vector3 first = ((Transform)targets[0]).localEulerAngles;
        for (int i = 1; i < targets.Length; i++)
        {
            if (((Transform)targets[i]).localEulerAngles != first)
            {
                return true;
            }
        }

        return false;
    }
}
