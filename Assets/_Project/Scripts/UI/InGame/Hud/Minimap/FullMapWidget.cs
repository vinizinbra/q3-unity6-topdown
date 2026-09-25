using Quantum;
using QuantumUser.View;
using UnityEngine;

// Full-level map overlay - the big map on its own, split out of the Tab-hold HeroInfoPopupWidget so
// a quick mid-fight map glance doesn't also bring up the whole stats readout. This class only owns
// showing/hiding root (same split as HeroInfoPopupWidget, which only owns its own Tab toggle):
// everything drawn INSIDE the panel - the shared map texture, POI icons, player/enemy markers - is
// still painted by MinimapWidget through its own fullMapImage/fullMapRect, since those clones are
// driven in lockstep with the corner minimap's (see MinimapWidget's OverlayPair).
//
// Toggled (not hold) by any of:
//   - toggleKey (keyboard, default M)
//   - R2 (OpenMiniMapTrigger, an analog trigger axis on Android pads - rising edge past
//     TriggerPressThreshold, so holding the trigger toggles once rather than every frame)
//   - OpenMiniMapToggle (a gamepad button)
//   - clicking any of toggleButtons (e.g. the corner minimap's own Button)
// and closed (never opened) by clicking any of closeButtons (e.g. an X inside root). While open,
// everything in hideWhileShown (the corner minimap) is hidden, and shown again on close.
// Lives on an always-active GameObject, never on root itself - QuantumGlobalMonoBehaviour.QUpdate
// doesn't run on an inactive GameObject, so a widget sitting on its own hidden root could never
// poll the input that opens it again.
public class FullMapWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Shown/hidden by the toggle - typically a backdrop plus MinimapWidget's fullMapImage/fullMapRect. If it carries a JuicyGameobject (PachaGames.Runtime), opening/closing scales in/out through it instead of an instant snap. Its authored active state (normally inactive) is the source of truth - read live, never tracked separately.")]
    private GameObject root;

    [SerializeField, Tooltip("Keyboard key that toggles the full map.")]
    private KeyCode toggleKey = KeyCode.M;

    [SerializeField, Tooltip("Buttons that toggle the full map on click - e.g. a Button covering the corner minimap. Optional.")]
    private UnityEngine.UI.Button[] toggleButtons;

    [SerializeField, Tooltip("Buttons that only ever close the full map on click - e.g. an X button inside root. Optional.")]
    private UnityEngine.UI.Button[] closeButtons;

    [SerializeField, Tooltip("Hidden (alpha 0, no raycasts) while the full map is open, shown again when it closes - e.g. the corner minimap's root. A CanvasGroup rather than SetActive on purpose: MinimapWidget lives under the corner minimap and is also what paints the full map, so deactivating it would freeze the full map too.")]
    private CanvasGroup[] hideWhileShown;

    // Named Input Manager button (ProjectSettings/InputManager.asset) for toggling from a gamepad -
    // unbound by default (Select now opens HeroInfoPopupWidget's OpenHeroInfo; R2 below still opens
    // the map). Assign a button in Project Settings > Input Manager, no code change.
    private static readonly string OpenMiniMapToggle = Quantum.GamepadInputNames.Get("OpenMiniMapToggle");

    // Named Input Manager Joystick Axis (default: 12th axis, any joystick) - R2 on most Android
    // Bluetooth pads (e.g. MOGA Pro 2; L2 is the 13th) is an analog trigger axis, not a button, so
    // it can't live on OpenMiniMapToggle. Which axis number a given pad uses varies - re-point it in
    // Project Settings > Input Manager if R2 doesn't respond.
    private static readonly string OpenMiniMapTrigger = Quantum.GamepadInputNames.Get("OpenMiniMapTrigger");
    private const float TriggerPressThreshold = 0.5f;
    private bool _triggerHeld;

    private JuicyGameobject _juicy;

    // A JuicyGameobject mid-hide is still activeSelf until its scale-out tween completes - counts as
    // already closed, so a second press during the tween isn't swallowed as another Hide.
    public bool IsShown => root != null && root.activeSelf && (_juicy == null || _juicy.hiding == false);

    // Awake, not Start: plain Unity UI wiring, not simulation-driven - same as the click-to-toggle
    // this replaces on MinimapWidget.
    private void Awake()
    {
        if (root != null)
            _juicy = root.GetComponent<JuicyGameobject>();

        AddListeners(toggleButtons, Toggle);
        AddListeners(closeButtons, Hide);
    }

    private static void AddListeners(UnityEngine.UI.Button[] buttons, UnityEngine.Events.UnityAction action)
    {
        if (buttons == null)
            return;

        foreach (UnityEngine.UI.Button button in buttons)
        {
            if (button != null)
                button.onClick.AddListener(action);
        }
    }

    public override void QUpdate(QuantumGame game)
    {
        // Pure visual toggle, polled from QUpdate rather than a raw Update() - see
        // HeroInfoPopupWidget.QUpdate. Fully-qualified: Quantum.Input also has this name.
        bool triggerDown = UnityEngine.Input.GetAxisRaw(OpenMiniMapTrigger) > TriggerPressThreshold;
        bool triggerPressed = triggerDown && _triggerHeld == false;
        _triggerHeld = triggerDown;

        if (triggerPressed || UnityEngine.Input.GetButtonDown(OpenMiniMapToggle) || UnityEngine.Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void Toggle()
    {
        SetShown(IsShown == false);
    }

    public void Hide()
    {
        SetShown(false);
    }

    public void SetShown(bool shown)
    {
        if (root == null)
            return;

        SetOthersHidden(shown);

        if (_juicy != null)
            _juicy.SetActive(shown);
        else if (root.activeSelf != shown)
            root.SetActive(shown);
    }

    private void SetOthersHidden(bool hidden)
    {
        if (hideWhileShown == null)
            return;

        foreach (CanvasGroup group in hideWhileShown)
        {
            if (group == null)
                continue;

            group.alpha = hidden ? 0f : 1f;
            group.blocksRaycasts = hidden == false;
            group.interactable = hidden == false;
        }
    }
}
