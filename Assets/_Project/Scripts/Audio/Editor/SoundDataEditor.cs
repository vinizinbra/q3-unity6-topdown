using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// Inspector for SoundData. The default one is a wall of ~20 fields where the two that matter most
// (which clips, how much variation) sit level with things touched once a year, so this is organised
// by how often each knob is actually reached for:
//
//   always visible  - audition buttons, the clip list (with per-clip trim/fade overrides), the
//                     group, volume/pitch variance
//   collapsed       - trim, fade, looping, 3D, voice limits, layers
//
// Sections remember their own open/closed state per SoundData, so opening a sound you were just
// tuning puts you back where you were rather than fully collapsed every time.
[CustomEditor(typeof(SoundData))]
[CanEditMultipleObjects]
public class SoundDataEditor : Editor
{
    private ReorderableList _clips;
    private ReorderableList _layers;

    private static readonly Color DropAreaColor = new Color(1f, 1f, 1f, 0.06f);

    private void OnEnable()
    {
        _drawn.Add("variants");
        _drawn.Add("clips");
        _drawn.Add("layers");
        _clips = BuildClipList();
        _layers = BuildLayerList();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawAuditionBar();
        EditorGUILayout.Space(4);

        _clips.DoLayoutList();
        DrawClipDropArea();

        // Only meaningful with something to choose between - a one-clip sound has no pick to make.
        using (new EditorGUI.DisabledScope(Prop("variants").arraySize < 2))
            EditorGUILayout.PropertyField(Prop("pick"));

        EditorGUILayout.Space(6);
        EditorGUILayout.PropertyField(Prop("group"));
        EditorGUILayout.PropertyField(Prop("spatial"));
        SerializedProperty localOnly = Prop("localPlayerOnly");
        EditorGUILayout.PropertyField(localOnly);

        // Meaningless once the sound is skipped outright for remote players - shown, but greyed, so
        // the override is visible rather than mysteriously having no effect.
        using (new EditorGUI.DisabledScope(localOnly.boolValue))
            EditorGUILayout.PropertyField(Prop("quieterWhenRemote"));

        EditorGUILayout.Space(6);
        DrawMinMax(Prop("volume"), "Volume", 0f, 1f);
        DrawMinMax(Prop("pitch"), "Pitch", 0.1f, 3f);

        DrawSection("Layers", () =>
        {
            EditorGUILayout.HelpBox(
                "Extra sounds played AT THE SAME TIME as this one - e.g. a skill impact plus an occasional voice line. Each layer is another SoundData with its own chance and delay.",
                MessageType.None);
            _layers.DoLayoutList();
        }, badge: Prop("layers").arraySize);

        DrawSection("Trim & Fade", () =>
        {
            EditorGUILayout.HelpBox(
                "Shared by every clip in this sound. A clip that needs its own can tick Trim or Fade on its row in the list above.",
                MessageType.None);
            DrawMinMax(Prop("delay"), "Start Delay", 0f, 5f);
            EditorGUILayout.PropertyField(Prop("startAt"));
            EditorGUILayout.PropertyField(Prop("endAt"));
            EditorGUILayout.PropertyField(Prop("fadeIn"));
            EditorGUILayout.PropertyField(Prop("fadeOut"));
            EditorGUILayout.PropertyField(Prop("loop"));
        });

        DrawSection("Limits & Routing", () =>
        {
            EditorGUILayout.PropertyField(Prop("cooldown"));
            EditorGUILayout.PropertyField(Prop("maxConcurrent"));
            EditorGUILayout.PropertyField(Prop("priority"));
            EditorGUILayout.PropertyField(Prop("useUnscaledTime"));
            EditorGUILayout.PropertyField(Prop("output"));
        });

        DrawUnhandledProperties();

        serializedObject.ApplyModifiedProperties();
    }

    private readonly HashSet<string> _drawn = new HashSet<string>();

    private SerializedProperty Prop(string name)
    {
        _drawn.Add(name);
        return serializedObject.FindProperty(name);
    }

    // A hand-written inspector silently hides any field it forgets to draw - which already happened
    // once here, with a new SoundData field invisible in the Inspector while working fine in code.
    // Rather than rely on remembering, anything not drawn above is listed here, so a forgotten field
    // shows up as an obvious loose end instead of disappearing.
    private void DrawUnhandledProperties()
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        bool anyDrawn = false;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (iterator.propertyPath == "m_Script" || _drawn.Contains(iterator.propertyPath))
                continue;

            if (anyDrawn == false)
            {
                anyDrawn = true;
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox("Fields not yet placed in this inspector - add them to SoundDataEditor.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(iterator, true);
        }
    }

    // ------------------------------------------------------------------ audition

    private void DrawAuditionBar()
    {
        // Auditioning several sounds at once would just overlap them into noise.
        using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
        using (new EditorGUILayout.HorizontalScope())
        {
            var data = (SoundData)target;

            if (GUILayout.Button("Play", GUILayout.Height(24)))
                SoundDataEditorPreview.PlayVariant(data);

            using (new EditorGUI.DisabledScope(data.variants == null || data.variants.Length < 2))
            {
                if (GUILayout.Button("Play Every Clip", GUILayout.Height(24)))
                    SoundDataEditorPreview.PlayEveryClip(data);
            }

            if (GUILayout.Button("Stop", GUILayout.Width(60), GUILayout.Height(24)))
                SoundDataEditorPreview.Stop(data);
        }
    }

    // ------------------------------------------------------------------ clips

    // One row per variation: the clip field always, plus - only when the row's own override is
    // ticked - that clip's private trim/fade. Collapsed by default so a set where every take lines
    // up still reads as the plain clip list it was before per-clip settings existed, and the extra
    // fields only take space on the rows actually using them.
    private ReorderableList BuildClipList()
    {
        SerializedProperty variants = serializedObject.FindProperty("variants");

        var list = new ReorderableList(serializedObject, variants, true, true, true, true);

        list.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, variants.arraySize == 1 ? "Clip" : $"Clips  ({variants.arraySize} variations)");
        };

        list.drawElementCallback = (rect, index, _, _) =>
        {
            SerializedProperty element = variants.GetArrayElementAtIndex(index);
            SerializedProperty trim = element.FindPropertyRelative("overrideTrim");
            SerializedProperty fade = element.FindPropertyRelative("overrideFade");

            float line = EditorGUIUtility.singleLineHeight;
            rect.y += 2f;
            rect.height = line;

            // Clip on the left, a play button plus the two override toggles on the right - so which
            // rows differ from the shared settings is visible at a glance down the list, and each
            // clip can be auditioned (with this sound's current volume/pitch/trim/fade) on its own.
            SerializedProperty clipProp = element.FindPropertyRelative("clip");
            var clipRect = new Rect(rect.x, rect.y, rect.width - 174f, line);
            EditorGUI.PropertyField(clipRect, clipProp, GUIContent.none);

            var playRect = new Rect(rect.xMax - 170f, rect.y, 28f, line);
            var trimRect = new Rect(rect.xMax - 136f, rect.y, 64f, line);
            var fadeRect = new Rect(rect.xMax - 68f, rect.y, 64f, line);

            using (new EditorGUI.DisabledScope(clipProp.objectReferenceValue == null))
            {
                if (GUI.Button(playRect, "▶"))
                    SoundDataEditorPreview.PlayClip((SoundData)target, clipProp.objectReferenceValue as AudioClip);
            }

            trim.boolValue = GUI.Toggle(trimRect, trim.boolValue, "Trim", EditorStyles.miniButton);
            fade.boolValue = GUI.Toggle(fadeRect, fade.boolValue, "Fade", EditorStyles.miniButton);

            using (new EditorGUI.IndentLevelScope())
            {
                if (trim.boolValue)
                {
                    rect.y += line + 2f;
                    EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, line), element.FindPropertyRelative("startAt"), new GUIContent("Start At"));
                    rect.y += line + 2f;
                    EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, line), element.FindPropertyRelative("endAt"), new GUIContent("End At"));
                }

                if (fade.boolValue)
                {
                    rect.y += line + 2f;
                    EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, line), element.FindPropertyRelative("fadeIn"), new GUIContent("Fade In"));
                    rect.y += line + 2f;
                    EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, line), element.FindPropertyRelative("fadeOut"), new GUIContent("Fade Out"));
                }
            }
        };

        // Variable height: a plain row is one line, each ticked override adds its two.
        list.elementHeightCallback = index =>
        {
            SerializedProperty element = variants.GetArrayElementAtIndex(index);
            float line = EditorGUIUtility.singleLineHeight + 2f;
            float height = line + 4f;

            if (element.FindPropertyRelative("overrideTrim").boolValue)
                height += line * 2f;

            if (element.FindPropertyRelative("overrideFade").boolValue)
                height += line * 2f;

            return height;
        };

        return list;
    }

    // Multi-clip drag-and-drop, since the whole point of a variation set is adding several at once -
    // the built-in list's + button only ever appends one empty slot at a time.
    private void DrawClipDropArea()
    {
        Rect area = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
        area.xMin += 16f;

        EditorGUI.DrawRect(area, DropAreaColor);
        GUI.Label(area, "Drop AudioClips here", EditorStyles.centeredGreyMiniLabel);

        Event evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;

        if (!area.Contains(evt.mousePosition))
            return;

        bool hasClip = false;
        foreach (Object dragged in DragAndDrop.objectReferences)
        {
            if (dragged is AudioClip)
            {
                hasClip = true;
                break;
            }
        }

        if (!hasClip)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type != EventType.DragPerform)
            return;

        DragAndDrop.AcceptDrag();

        SerializedProperty variants = Prop("variants");
        foreach (Object dragged in DragAndDrop.objectReferences)
        {
            if (dragged is not AudioClip clip)
                continue;

            variants.arraySize++;
            SerializedProperty added = variants.GetArrayElementAtIndex(variants.arraySize - 1);
            added.FindPropertyRelative("clip").objectReferenceValue = clip;
            // A new array element copies the one before it, so clear the overrides explicitly -
            // otherwise a clip dropped after one with a custom trim silently inherits that trim.
            added.FindPropertyRelative("overrideTrim").boolValue = false;
            added.FindPropertyRelative("overrideFade").boolValue = false;
        }

        serializedObject.ApplyModifiedProperties();
        evt.Use();
    }

    // ------------------------------------------------------------------ layers

    private ReorderableList BuildLayerList()
    {
        SerializedProperty layers = serializedObject.FindProperty("layers");

        var list = new ReorderableList(serializedObject, layers, true, false, true, true);

        list.drawElementCallback = (rect, index, _, _) =>
        {
            SerializedProperty element = layers.GetArrayElementAtIndex(index);
            SerializedProperty sound = element.FindPropertyRelative("sound");
            SerializedProperty chance = element.FindPropertyRelative("chance");

            float line = EditorGUIUtility.singleLineHeight;
            rect.y += 3f;
            rect.height = line;

            EditorGUI.PropertyField(rect, sound, GUIContent.none);

            rect.y += line + 2f;
            EditorGUI.PropertyField(rect, chance, new GUIContent("Chance"));

            rect.y += line + 2f;
            DrawMinMax(rect, element.FindPropertyRelative("delay"), "Delay", 0f, 3f);

            rect.y += line + 2f;
            EditorGUI.PropertyField(rect, element.FindPropertyRelative("volumeScale"), new GUIContent("Volume"));
        };

        list.elementHeight = (EditorGUIUtility.singleLineHeight + 2f) * 4f + 6f;
        return list;
    }

    // ------------------------------------------------------------------ widgets

    // A Vector2 used as a range reads terribly as two unlabelled floats, so draw it as what it is:
    // a slider with both ends editable. Equal ends = a fixed value, which is the common case.
    private static void DrawMinMax(SerializedProperty prop, string label, float min, float max)
    {
        Rect rect = EditorGUILayout.GetControlRect();
        DrawMinMax(rect, prop, label, min, max);
    }

    private static void DrawMinMax(Rect rect, SerializedProperty prop, string label, float min, float max)
    {
        Vector2 value = prop.vector2Value;

        Rect content = EditorGUI.PrefixLabel(rect, new GUIContent(label, prop.tooltip));

        const float FieldWidth = 46f;
        const float Gap = 4f;

        var leftField = new Rect(content.x, content.y, FieldWidth, content.height);
        var slider = new Rect(content.x + FieldWidth + Gap, content.y,
            content.width - (FieldWidth + Gap) * 2f, content.height);
        var rightField = new Rect(content.xMax - FieldWidth, content.y, FieldWidth, content.height);

        EditorGUI.BeginChangeCheck();

        float low = EditorGUI.FloatField(leftField, value.x);
        float high = EditorGUI.FloatField(rightField, value.y);
        EditorGUI.MinMaxSlider(slider, ref low, ref high, min, max);

        if (EditorGUI.EndChangeCheck())
        {
            low = Mathf.Clamp(low, min, max);
            high = Mathf.Clamp(high, low, max);
            prop.vector2Value = new Vector2(low, high);
        }
    }

    // Collapsed by default and remembered per asset, so the everyday fields stay visible and the rest is
    // one click away rather than gone.
    private void DrawSection(string title, System.Action body, int badge = 0)
    {
        string key = $"SoundDataEditor.{target.GetInstanceID()}.{title}";
        bool open = SessionState.GetBool(key, false);

        string header = badge > 0 ? $"{title}  ({badge})" : title;

        EditorGUILayout.Space(2);
        bool next = EditorGUILayout.Foldout(open, header, true, EditorStyles.foldoutHeader);
        if (next != open)
            SessionState.SetBool(key, next);

        if (!next)
            return;

        using (new EditorGUI.IndentLevelScope())
            body();
    }
}
