using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One level of the menu's cascading gamepad navigation (main menu -> tab content -> detail panel...).
/// While a scope is active, directional navigation only moves between its own Selectables
/// (see <see cref="MenuNavigationController"/>); Submit on an item enters a child scope via
/// <see cref="Enter"/>, and Cancel returns to <see cref="ReturnTo"/> in the parent.
/// A scope's own Selectables are the ones under it that don't belong to a nested child scope.
/// </summary>
public class FocusScopeWidget : MonoBehaviour
{
    private static readonly List<FocusScopeWidget> Active = new List<FocusScopeWidget>();

    [SerializeField, Tooltip("The scope Cancel returns to. Empty for the root (main menu) scope.")]
    private FocusScopeWidget parent;

    [SerializeField, Tooltip("Where focus lands when this scope is entered, when nothing more specific applies (a TabContent's DefaultFocus wins). Empty = first interactable.")]
    private Selectable defaultTarget;

    public FocusScopeWidget Parent => parent;

    /// <summary>The Selectable that entered this scope - Cancel puts focus back on it.</summary>
    public Selectable ReturnTo { get; private set; }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    public bool Contains(GameObject go)
    {
        if (go == null || !go.transform.IsChildOf(transform))
            return false;

        foreach (FocusScopeWidget scope in Active)
        {
            if (scope != this && scope.parent == this && go.transform.IsChildOf(scope.transform))
                return false;
        }

        return true;
    }

    /// <summary>The deepest active scope that owns this object, or null when it isn't part of any.</summary>
    public static FocusScopeWidget Find(GameObject go)
    {
        foreach (FocusScopeWidget scope in Active)
        {
            if (scope.Contains(go))
                return scope;
        }

        return null;
    }

    /// <summary>
    /// Steps into this scope. Does nothing (returns false) when the scope has nothing usable to focus - an info panel
    /// without buttons - since entering it would strand focus on an empty level.
    /// </summary>
    public bool Enter(Selectable returnTo)
    {
        if (ResolveDefault() == null)
            return false;

        ReturnTo = returnTo;

        if (MenuNavigationController.Instance != null)
            MenuNavigationController.Instance.Activate(this);

        return Focus();
    }

    /// <summary>Focuses this scope's default target. False when it has nothing usable to focus.</summary>
    public bool Focus()
    {
        Selectable target = ResolveDefault();
        if (target == null || EventSystem.current == null)
            return false;

        EventSystem.current.SetSelectedGameObject(target.gameObject);
        return true;
    }

    private Selectable ResolveDefault()
    {
        var tab = GetComponent<TabContent>();
        if (tab != null && IsUsable(tab.DefaultFocus))
            return tab.DefaultFocus;

        if (IsUsable(defaultTarget))
            return defaultTarget;

        foreach (Selectable selectable in GetComponentsInChildren<Selectable>(false))
        {
            if (IsUsable(selectable) && Contains(selectable.gameObject))
                return selectable;
        }

        return null;
    }

    private static bool IsUsable(Selectable selectable)
    {
        return selectable != null && selectable.interactable && selectable.gameObject.activeInHierarchy;
    }
}
