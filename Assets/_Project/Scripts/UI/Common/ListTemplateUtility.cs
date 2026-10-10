using UnityEngine;

/// <summary>
/// In-scene list templates: each list keeps one card / row / tile (a prefab instance) as a child of its layout, so it
/// can be seen and edited in the scene. At runtime the list clones that template once per item and hides the template.
/// </summary>
public static class ListTemplateUtility
{
    /// <summary>Clones the template under <paramref name="parent"/> and makes sure the clone is active.</summary>
    public static T Spawn<T>(T template, Transform parent) where T : Component
    {
        T item = Object.Instantiate(template, parent);
        item.gameObject.SetActive(true);
        return item;
    }

    /// <summary>Hides the templates (they stay in the scene, ignored by their layouts while inactive).</summary>
    public static void Hide(params Component[] templates)
    {
        foreach (Component template in templates)
        {
            if (template != null)
                template.gameObject.SetActive(false);
        }
    }

    /// <summary>Destroys every child of <paramref name="root"/> except the given templates (previous clones).</summary>
    public static void Clear(Transform root, params Component[] keep)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            if (IsTemplate(child, keep))
                continue;
            child.SetActive(false);
            Object.Destroy(child);
        }
    }

    private static bool IsTemplate(GameObject go, Component[] templates)
    {
        foreach (Component template in templates)
        {
            if (template != null && template.gameObject == go)
                return true;
        }

        return false;
    }
}
