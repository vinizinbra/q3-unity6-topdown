using System;
using System.Reflection;
using NaughtyAttributes.Editor;
using UnityEditor;
using UnityEngine;

// Draws an IntroSequence step like the default drawer, plus a Play/Stop preview button directly
// under its Audio field so a clip can be auditioned without entering Play Mode.
//
// Children are drawn by hand (rather than delegating to the default drawer) because the button has
// to sit in the middle of the field list, right after Audio.
[CustomPropertyDrawer(typeof(IntroSequence.Step))]
public class IntroSequenceStepDrawer : PropertyDrawer
{
    private const string AudioField = nameof(IntroSequence.Step.Audio);
    private const string NameField = nameof(IntroSequence.Step.Name);

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;

        if (property.isExpanded == false)
            return height;

        foreach (SerializedProperty child in Children(property))
        {
            height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);

            if (child.name == AudioField)
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // "Step 3 - Name" in the header so a collapsed list is still readable.
        SerializedProperty nameProp = property.FindPropertyRelative(NameField);
        string title = string.IsNullOrEmpty(nameProp?.stringValue) == false
            ? $"{label.text} - {nameProp.stringValue}"
            : label.text;

        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, title, true);

        if (property.isExpanded == true)
        {
            EditorGUI.indentLevel++;
            float y = line.yMax + EditorGUIUtility.standardVerticalSpacing;

            foreach (SerializedProperty child in Children(property))
            {
                float childHeight = EditorGUI.GetPropertyHeight(child, true);
                // NaughtyAttributes' own PropertyField (not EditorGUI's) so its meta attributes -
                // [ReadOnly] on the computed Duration/silence fields - still apply inside this drawer.
                NaughtyEditorGUI.PropertyField(new Rect(position.x, y, position.width, childHeight), child, true);
                y += childHeight + EditorGUIUtility.standardVerticalSpacing;

                if (child.name == AudioField)
                {
                    Rect buttonRow = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                    DrawPreviewButton(buttonRow, child.objectReferenceValue as AudioClip);
                    y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static void DrawPreviewButton(Rect row, AudioClip clip)
    {
        // Indented like a labelled field so the buttons line up under the Audio value column.
        Rect area = EditorGUI.IndentedRect(row);
        float half = (area.width - 4f) * 0.5f;

        using (new EditorGUI.DisabledScope(clip == null))
        {
            if (GUI.Button(new Rect(area.x, area.y, half, area.height), "▶ Play"))
            {
                InvokeAudioUtil("StopAllPreviewClips");
                InvokeAudioUtil("PlayPreviewClip", new object[] { clip, 0, false }, typeof(AudioClip), typeof(int), typeof(bool));
            }
        }

        if (GUI.Button(new Rect(area.x + half + 4f, area.y, half, area.height), "■ Stop"))
            InvokeAudioUtil("StopAllPreviewClips");
    }

    // Immediate children only - iterating with enterChildren=true would also walk into the
    // AudioClip/Sprite object references' own internals and duplicate rows.
    private static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty parent)
    {
        SerializedProperty iterator = parent.Copy();
        SerializedProperty end = iterator.GetEndProperty();

        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren) && SerializedProperty.EqualContents(iterator, end) == false)
        {
            enterChildren = false;
            yield return iterator;
        }
    }

    // Editor audio preview lives on the internal UnityEditor.AudioUtil, reflected since it isn't
    // public API - same approach as SoundClipPickerWindow.
    private static void InvokeAudioUtil(string method, object[] args = null, params Type[] signature)
    {
        Type type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        MethodInfo info = type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, signature, null);

        if (info == null)
        {
            Debug.LogWarning($"[IntroSequenceStepDrawer] UnityEditor.AudioUtil.{method} not found on this Unity version - preview playback is unavailable.");
            return;
        }

        info.Invoke(null, args ?? Array.Empty<object>());
    }
}
