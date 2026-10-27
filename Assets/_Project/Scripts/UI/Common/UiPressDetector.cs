using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Raises Pressed for any press or submit on an interactable click target (Button, Toggle, tabs, cards...) - found by
/// raycasting the EventSystem the same way a click would, so it follows whatever the UI already does. Control Freak
/// 2's touch panel only handles pointer down/up (never click), so the gameplay joystick/buttons never trigger it.
/// Self-installed at startup and kept across scenes; listeners (HapticsDirector, ButtonPressDirector) just subscribe.
/// </summary>
public class UiPressDetector : MonoBehaviour
{
    // The click/submit handler that was pressed. It may already be inactive by the time this fires (see LateUpdate).
    public static event Action<GameObject> Pressed;

    private static UiPressDetector instance;

    private readonly List<RaycastResult> raycastResults = new();
    private PointerEventData pointerData;
    private GameObject submitTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(UiPressDetector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UiPressDetector>();
    }

    private void Update()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        if (TryGetPressPosition(out Vector2 position))
        {
            GameObject target = GetClickTargetAt(eventSystem, position);
            if (target != null)
                Pressed?.Invoke(target);
        }
        else if (SubmitPressed(eventSystem) && submitTarget != null)
        {
            Pressed?.Invoke(submitTarget);
        }
    }

    // Submit acts on the press frame itself, inside EventSystem.Update - which may run before this Update and has
    // already clicked the button by then (an upgrade card disables itself the moment it is picked). So the target
    // is what was selected and pressable at the end of the previous frame.
    private void LateUpdate()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject handler = eventSystem != null ? ExecuteEvents.GetEventHandler<ISubmitHandler>(eventSystem.currentSelectedGameObject) : null;
        submitTarget = IsInteractable(handler) ? handler : null;
    }

    // Every touch, not just Pointer.current's primary one: mid-match the first finger is usually on the joystick
    // and the button press is the second.
    private static bool TryGetPressPosition(out Vector2 position)
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    position = touch.position.ReadValue();
                    return true;
                }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            position = mouse.position.ReadValue();
            return true;
        }

        position = default;
        return false;
    }

    private static bool SubmitPressed(EventSystem eventSystem)
    {
        if (UiKeyboardSubmit.PressedThisFrame)
            return true;

        var module = eventSystem.currentInputModule as InputSystemUIInputModule;
        InputAction submit = module != null && module.submit != null ? module.submit.action : null;
        return submit != null && submit.WasPressedThisFrame();
    }

    // Topmost hit only, like the click itself: a button under a popup's dim background isn't what was pressed.
    private GameObject GetClickTargetAt(EventSystem eventSystem, Vector2 position)
    {
        pointerData ??= new PointerEventData(eventSystem);
        pointerData.Reset();
        pointerData.position = position;

        raycastResults.Clear();
        eventSystem.RaycastAll(pointerData, raycastResults);
        if (raycastResults.Count == 0)
            return null;

        GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(raycastResults[0].gameObject);
        return IsInteractable(handler) ? handler : null;
    }

    // A handler without a Selectable (a custom card) counts; a disabled button doesn't.
    private static bool IsInteractable(GameObject handler)
    {
        if (handler == null)
            return false;

        return handler.TryGetComponent(out Selectable selectable) == false || selectable.IsInteractable();
    }
}
