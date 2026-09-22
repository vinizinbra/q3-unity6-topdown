using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Bulk-adds SelectableScaleWidget to every Selectable (Button/Toggle/Slider/Scrollbar/Dropdown/
// InputField/...) currently loaded - open scene(s) or a prefab open in Prefab Mode - that doesn't
// already have one, so gamepad/joystick selection feedback lands everywhere without hand-wiring
// each element one at a time. Safe to re-run: elements that already have the widget are skipped.
public static class SelectableScaleWidgetAdder
{
    [MenuItem("Tools/RiftRaiders/Navigation/Add All In Scene")]
    private static void AddToAll()
    {
        var selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirtyScenes = new HashSet<Scene>();
        int added = 0;

        foreach (Selectable selectable in selectables)
        {
            if (selectable.GetComponent<SelectableScaleWidget>() != null)
                continue;

            Undo.AddComponent<SelectableScaleWidget>(selectable.gameObject);
            dirtyScenes.Add(selectable.gameObject.scene);
            added++;
        }

        foreach (Scene scene in dirtyScenes)
        {
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[SelectableScaleWidget] added to {added} interactable(s) across {dirtyScenes.Count} scene(s) - skipped {selectables.Length - added} that already had it.");
    }
}
