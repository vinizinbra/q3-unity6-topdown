using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drives the menu's cascading gamepad navigation:
///  - only the active <see cref="FocusScopeWidget"/> is directionally navigable (every other scoped
///    Selectable temporarily gets Navigation.None, so the stick can't leak between levels);
///  - Cancel goes back one level (detail -> list -> main menu);
///  - L1/R1 switch tabs.
/// Selectables that belong to no scope (popups, top bar) are left alone.
/// Uses the legacy Input Manager, like the rest of the menu's StandaloneInputModule.
/// </summary>
public class MenuNavigationController : MonoBehaviour
{
    public static MenuNavigationController Instance { get; private set; }

    [SerializeField] private FocusScopeWidget rootScope;
    [SerializeField] private TabGroup tabGroup;

    private readonly Dictionary<Selectable, Navigation> savedNavigation = new Dictionary<Selectable, Navigation>();
    private FocusScopeWidget active;
    private GameObject lastSelected;
    private int lastSelectableCount = -1;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        active = rootScope;
        Apply();
    }

    public void Activate(FocusScopeWidget scope)
    {
        active = scope;
        Apply();
    }

    private void Update()
    {
        if (EventSystem.current == null)
            return;

        if (active == null || !active.gameObject.activeInHierarchy)
        {
            active = rootScope;
            Apply();
        }
        else if (Selectable.allSelectableCount != lastSelectableCount)
        {
            Apply();
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != lastSelected)
        {
            lastSelected = selected;
            FollowSelection(selected);
        }

        if (selected == null || !selected.activeInHierarchy)
            WakeUpOnInput();

        if (Input.GetButtonDown("Cancel"))
            Back();

        // L1/R1 on a gamepad, [ and ] on a keyboard.
        if (Input.GetKeyDown(KeyCode.JoystickButton4) || Input.GetKeyDown(KeyCode.LeftBracket))
            SwitchTab(-1);
        else if (Input.GetKeyDown(KeyCode.JoystickButton5) || Input.GetKeyDown(KeyCode.RightBracket))
            SwitchTab(1);
    }

    // A mouse click can land focus in another scope - make that the active one so nav and Cancel follow it.
    private void FollowSelection(GameObject selected)
    {
        if (selected == null)
            return;

        FocusScopeWidget scope = FocusScopeWidget.Find(selected);
        if (scope != null && scope != active)
        {
            active = scope;
            Apply();
        }
    }

    // With nothing focused (mouse user, a screen change, or UGUI clearing the selection of a button that just
    // went non-interactable) the first stick/Submit input picks up focus again. If the active scope has
    // nothing usable left to focus, it walks up the parents so input can never end up stranded.
    private void WakeUpOnInput()
    {
        bool input = Input.GetAxisRaw("Vertical") != 0f || Input.GetAxisRaw("Horizontal") != 0f || Input.GetButtonDown("Submit");
        if (!input)
            return;

        FocusScopeWidget scope = active != null ? active : rootScope;
        while (scope != null)
        {
            if (scope == rootScope && tabGroup != null && tabGroup.selectedTabButton != null)
            {
                Activate(scope);
                Select(tabGroup.selectedTabButton.GetComponent<Selectable>());
                return;
            }

            if (scope.Focus())
            {
                if (scope != active)
                    Activate(scope);
                return;
            }

            scope = scope.Parent;
        }
    }

    private void Back()
    {
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (active == null || active.Parent == null || selected == null || !active.Contains(selected))
            return;

        FocusScopeWidget leaving = active;
        active = leaving.Parent;
        Apply();

        Selectable target = leaving.ReturnTo;
        if (target != null && target.interactable && target.gameObject.activeInHierarchy)
            Select(target);
        else if (active == rootScope && tabGroup != null && tabGroup.selectedTabButton != null)
            Select(tabGroup.selectedTabButton.GetComponent<Selectable>());
        else
            active.Focus();
    }

    private void SwitchTab(int direction)
    {
        if (tabGroup == null || tabGroup.tabButtons == null || tabGroup.tabButtons.Count == 0)
            return;

        int count = tabGroup.tabButtons.Count;
        int current = Mathf.Max(0, tabGroup.tabButtons.IndexOf(tabGroup.selectedTabButton));

        for (int step = 1; step <= count; step++)
        {
            int index = ((current + direction * step) % count + count) % count;
            if (index >= tabGroup.tabContent.Count || tabGroup.tabContent[index] == null)
                continue;

            TabButton button = tabGroup.tabButtons[index];
            bool insideContent = active != rootScope;
            tabGroup.OnTabSelected(button, insideContent);

            if (!insideContent)
                Select(button.GetComponent<Selectable>());
            return;
        }
    }

    private static void Select(Selectable target)
    {
        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    // Everything scoped but outside the active scope stops being a navigation target.
    private void Apply()
    {
        lastSelectableCount = Selectable.allSelectableCount;

        foreach (Selectable selectable in Selectable.allSelectablesArray)
        {
            if (selectable == null)
                continue;

            FocusScopeWidget owner = FocusScopeWidget.Find(selectable.gameObject);
            if (owner == null)
                continue;

            if (owner == active)
            {
                if (savedNavigation.TryGetValue(selectable, out Navigation original))
                {
                    selectable.navigation = original;
                    savedNavigation.Remove(selectable);
                }
            }
            else if (!savedNavigation.ContainsKey(selectable))
            {
                savedNavigation[selectable] = selectable.navigation;
                Navigation none = selectable.navigation;
                none.mode = Navigation.Mode.None;
                selectable.navigation = none;
            }
        }
    }
}
