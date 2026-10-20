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
    private GameObject selectedAtFrameStart;
    private Vector2 lastNavigate;

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

    // Runs after every Update, so after the EventSystem has applied this frame's stick move whatever the
    // script order. Two rules on top of uGUI's own navigation:
    //  - a move may not leave the menu: Automatic navigation also finds unscoped Selectables on other
    //    canvases (debug panels, the cheat menu, a prefab open in Prefab Mode), which left focus on
    //    something invisible. Such a move is undone;
    //  - Left at the left edge of a level (nothing there to move to) goes back one level, like Cancel -
    //    the mirror of Right on a rail button stepping into its tab.
    private void LateUpdate()
    {
        GameObject before = selectedAtFrameStart;
        Vector2 navigate = Quantum.GamepadControls.Navigate;
        bool leftPress = IsLeft(navigate) && !IsLeft(lastNavigate);
        lastNavigate = navigate;

        EventSystem eventSystem = EventSystem.current;
        selectedAtFrameStart = eventSystem != null ? eventSystem.currentSelectedGameObject : null;

        if (eventSystem == null || (menuCanvas != null && !menuCanvas.enabled) || PopupOpen || popupWasOpen || typingWasActive)
            return;
        if (before == null || !before.activeInHierarchy || active == null || !active.Contains(before))
            return;

        GameObject now = eventSystem.currentSelectedGameObject;
        if (now != before && now != null && navigate != Vector2.zero && !Quantum.GamepadControls.SubmitPressed &&
            FocusScopeWidget.Find(now) == null)
        {
            // Redo the move with everything outside the menu out of the search, so the nearest menu item in that
            // direction still wins (e.g. Left from Play reaches the hero picker, not a debug button past it).
            Selectable inMenu = FindInMenu(before.GetComponent<Selectable>(), navigate);
            now = inMenu != null ? inMenu.gameObject : before;
            eventSystem.SetSelectedGameObject(now);
        }

        if (leftPress && now == before && !ConsumesHorizontal(before))
            Back();

        selectedAtFrameStart = eventSystem.currentSelectedGameObject;
    }

    private readonly List<KeyValuePair<Selectable, Navigation>> hiddenFromSearch = new List<KeyValuePair<Selectable, Navigation>>();

    // uGUI's own neighbour search from `from` in the stick's direction, with every unscoped Selectable
    // temporarily non-navigable (FindSelectable skips Navigation.None) - only runs on a frame a move leaked.
    private Selectable FindInMenu(Selectable from, Vector2 navigate)
    {
        if (from == null)
            return null;

        hiddenFromSearch.Clear();
        foreach (Selectable selectable in Selectable.allSelectablesArray)
        {
            if (selectable == null || selectable.navigation.mode == Navigation.Mode.None || FocusScopeWidget.Find(selectable.gameObject) != null)
                continue;

            hiddenFromSearch.Add(new KeyValuePair<Selectable, Navigation>(selectable, selectable.navigation));
            Navigation none = selectable.navigation;
            none.mode = Navigation.Mode.None;
            selectable.navigation = none;
        }

        Selectable found;
        if (Mathf.Abs(navigate.x) >= Mathf.Abs(navigate.y))
            found = navigate.x < 0f ? from.FindSelectableOnLeft() : from.FindSelectableOnRight();
        else
            found = navigate.y < 0f ? from.FindSelectableOnDown() : from.FindSelectableOnUp();

        foreach (KeyValuePair<Selectable, Navigation> hidden in hiddenFromSearch)
            hidden.Key.navigation = hidden.Value;
        hiddenFromSearch.Clear();

        return found != null && found.IsInteractable() && FocusScopeWidget.Find(found.gameObject) != null ? found : null;
    }

    private static bool IsLeft(Vector2 navigate) => navigate.x < -0.5f && Mathf.Abs(navigate.x) > Mathf.Abs(navigate.y);

    // Controls that use Left themselves (a horizontal slider changes its value), so Left on them never goes back.
    private static bool ConsumesHorizontal(GameObject go)
    {
        Slider slider = go.GetComponent<Slider>();
        if (slider != null && slider.IsInteractable() &&
            (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft))
            return true;

        Scrollbar scrollbar = go.GetComponent<Scrollbar>();
        return scrollbar != null && scrollbar.IsInteractable() &&
               (scrollbar.direction == Scrollbar.Direction.LeftToRight || scrollbar.direction == Scrollbar.Direction.RightToLeft);
    }

    /// <summary>
    /// Sets a scoped Selectable's navigation. Use this instead of assigning <c>navigation</c> directly: while
    /// the Selectable's scope is inactive this controller holds its real navigation aside (it is set to None),
    /// and a direct assignment would be overwritten when the scope becomes active again.
    /// </summary>
    public static void SetNavigation(Selectable selectable, Navigation navigation)
    {
        if (selectable == null)
            return;

        if (Instance != null && Instance.savedNavigation.ContainsKey(selectable))
            Instance.savedNavigation[selectable] = navigation;
        else
            selectable.navigation = navigation;
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
