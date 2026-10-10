using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drives the menu's cascading gamepad navigation:
///  - only the active <see cref="FocusScopeWidget"/> is directionally navigable (every other scoped
///    Selectable temporarily gets Navigation.None, so the stick can't leak between levels);
///  - Cancel goes back one level (detail -> list -> main menu);
///  - L1/R1 (or [ and ]) switch the sub-tabs of the open tab (Power/Exploration, Enemies/Bosses...);
///  - while a menu popup is open the menu behind it is not navigable and Cancel is left to the popup;
///  - does nothing while the menu Canvas is switched off (a match is running).
/// Selectables that belong to no scope (popups, top bar) are left alone.
/// Reads Quantum.GamepadControls (Input System), like the menu's InputSystemUIInputModule.
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
    private Canvas menuCanvas;
    private bool modal;
    private bool popupWasOpen;
    private bool typingWasActive;
    private GameObject focusBeforePopup;

    private void Awake()
    {
        Instance = this;
        menuCanvas = GetComponentInParent<Canvas>(true);
        if (menuCanvas != null)
            menuCanvas = menuCanvas.rootCanvas;
    }

    private static bool PopupOpen => PopupManager.instance != null && PopupManager.instance.currentPopup != null;

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

        // The menu Canvas is only switched off (not unloaded) while a match runs - then none of this applies.
        if (menuCanvas != null && !menuCanvas.enabled)
            return;

        UpdateModal();

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

        // A text field being edited owns input: Cancel ends the edit (not "go back"), and L1/R1 - which are
        // also [ and ] - are just characters.
        // It also counts for one frame after the edit ended: the field can end it itself earlier in the same frame,
        // and that Cancel must not then also go back a level.
        bool editing = SubmitToEditInputField.IsEditing(selected);
        bool swallowEdit = editing || typingWasActive;
        typingWasActive = editing;
        if (swallowEdit)
        {
            if (editing && Quantum.GamepadControls.CancelPressed)
                selected.GetComponent<TMPro.TMP_InputField>().DeactivateInputField();
            return;
        }

        // A popup owns input: the menu behind it must not react to the same Cancel / stick press. It also
        // stays "open" for one frame after closing, so the Cancel that closed it isn't handled twice.
        bool swallow = PopupOpen || popupWasOpen;
        popupWasOpen = PopupOpen;
        if (swallow)
            return;

        if (selected == null || !selected.activeInHierarchy)
            WakeUpOnInput();

        if (Quantum.GamepadControls.CancelPressed)
            Back();

        // L1/R1 on a gamepad, [ and ] on a keyboard.
        if (Quantum.GamepadControls.TabPrevPressed)
            SwitchSubTab(-1);
        else if (Quantum.GamepadControls.TabNextPressed)
            SwitchSubTab(1);
    }

    // A menu popup makes every scoped Selectable non-navigable (so the stick can't reach the menu behind
    // the dim) and hands focus back to whatever had it once the popup is gone.
    private void UpdateModal()
    {
        bool open = PopupOpen;
        if (open == modal)
            return;

        modal = open;
        if (open)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            focusBeforePopup = selected != null && FocusScopeWidget.Find(selected) != null ? selected : null;
        }
        else if (focusBeforePopup != null)
        {
            Selectable restore = focusBeforePopup.GetComponent<Selectable>();
            focusBeforePopup = null;
            if (restore != null && restore.IsInteractable() && restore.gameObject.activeInHierarchy)
                Select(restore);
        }

        Apply();
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
        bool input = Quantum.GamepadControls.NavigateActive || Quantum.GamepadControls.SubmitPressed;
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

    // L1/R1: switches the sub-tab strip of the tab that is open (Power/Exploration, Enemies/Bosses/..,
    // Skill/Mastery, Weapons/Perks). Tabs without one (Home) ignore it. If the switch hides the page that
    // held focus, focus moves to the new page's default item, or to the strip's own button when the new
    // page has nothing focusable.
    private void SwitchSubTab(int direction)
    {
        TabContent content = tabGroup != null ? tabGroup.selectedTabContent : null;
        TabStripWidget strip = content != null ? content.SubTabs : null;
        if (strip == null || !strip.Step(direction))
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy)
            return;

        Selectable target = content.DefaultFocus;
        if (target == null || !target.IsInteractable() || !target.gameObject.activeInHierarchy)
            target = strip.SelectedButton;
        if (target == null)
            return;

        FocusScopeWidget scope = FocusScopeWidget.Find(target.gameObject);
        if (scope != null && scope != active)
            Activate(scope);

        Select(target);
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

            if (owner == active && !modal)
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
