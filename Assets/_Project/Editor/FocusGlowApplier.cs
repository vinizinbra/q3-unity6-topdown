using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds <see cref="FocusOutlineWidget"/> (the gamepad/keyboard focus glow) - and makes text fields <see cref="SubmitToEditInputField"/> - for every focusable element of the
/// open scene(s), then runs <see cref="FocusOutlineGenerator"/> so every background sprite in use has its glow
/// sprite. Idempotent: elements that already have the widget are left alone (their color is never touched).
/// Run it again after building new UI; for prefabs, add the widget by hand or open the prefab scene first.
///
/// Rules (see <see cref="ShouldGlow"/> / <see cref="ColorFor"/>):
///  - Button, Toggle, TMP_Dropdown and TMP_InputField with a background Image that has a sprite.
///    Sliders/Scrollbars have no single shape to outline and are skipped.
///  - Skipped: dropdown templates, debug UI, full-screen dim buttons, anything named in <see cref="SkipNames"/>.
///  - Color: white around the pink/green call-to-action buttons (a pink glow would vanish into them),
///    pink everywhere else.
/// </summary>
public static class FocusGlowApplier
{
    private static readonly string[] SkipNames = { "Dim", "MiniMapMask" };
    private static readonly string[] SkipPathParts = { "Template", "Debug" };

    private static readonly Color Pink = new Color(0.992f, 0.224f, 0.443f);
    private static readonly Color White = Color.white;

    [MenuItem("Tools/RiftRaiders/UI/Add Focus Glow To Open Scenes")]
    public static void Apply()
    {
        int added = 0, already = 0, skipped = 0;
        var touched = new HashSet<Scene>();

        foreach (Selectable selectable in Resources.FindObjectsOfTypeAll<Selectable>())
        {
            if (!selectable.gameObject.scene.IsValid() || EditorUtility.IsPersistent(selectable))
                continue;

            if (!ShouldGlow(selectable, out Image background))
            {
                skipped++;
                continue;
            }

            if (selectable.GetComponent<FocusOutlineWidget>() != null)
            {
                already++;
                continue;
            }

            var widget = Undo.AddComponent<FocusOutlineWidget>(selectable.gameObject);
            var so = new SerializedObject(widget);
            so.FindProperty("color").colorValue = ColorFor(background);
            so.ApplyModifiedProperties();

            touched.Add(selectable.gameObject.scene);
            added++;
        }

        // Text fields also need to wait for Submit before they start editing (see SubmitToEditInputField): any plain
        // TMP_InputField is switched over to that subclass (same serialized fields, the script is just swapped).
        MonoScript submitToEdit = FindScript(nameof(SubmitToEditInputField));
        foreach (TMP_InputField input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (!input.gameObject.scene.IsValid() || EditorUtility.IsPersistent(input) || input is SubmitToEditInputField || submitToEdit == null)
                continue;

            var so = new SerializedObject(input);
            so.FindProperty("m_Script").objectReferenceValue = submitToEdit;
            so.ApplyModifiedPropertiesWithoutUndo();
            touched.Add(input.gameObject.scene);
        }

        foreach (Scene scene in touched)
            EditorSceneManager.MarkSceneDirty(scene);

        FocusOutlineGenerator.Generate();
        Debug.Log($"[FocusGlow] Added {added}, already had {already}, skipped {skipped}. Save the scene(s) to keep the new widgets.");
    }

    private static MonoScript FindScript(string typeName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript " + typeName))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.name == typeName)
                return script;
        }

        return null;
    }

    private static bool ShouldGlow(Selectable selectable, out Image background)
    {
        background = null;

        if (!(selectable is Button || selectable is Toggle || selectable is TMP_Dropdown || selectable is TMP_InputField))
            return false;

        foreach (string name in SkipNames)
        {
            if (selectable.name == name)
                return false;
        }

        for (Transform t = selectable.transform; t != null; t = t.parent)
        {
            foreach (string part in SkipPathParts)
            {
                if (t.name.Contains(part))
                    return false;
            }
        }

        // The widget itself picks the background the same way (own Image, else the target graphic).
        background = selectable.GetComponent<Image>() ?? selectable.targetGraphic as Image;
        return background != null && background.sprite != null;
    }

    private static Color ColorFor(Image background)
    {
        string sprite = background.sprite.name;
        return sprite.Contains("Pink") || sprite.Contains("Green") ? White : Pink;
    }
}
