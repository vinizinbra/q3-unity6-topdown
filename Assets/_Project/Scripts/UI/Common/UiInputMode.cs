using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Whether the UI is being driven by navigation (gamepad / keyboard: Navigate, Submit, Cancel, tab switching) or by a
/// pointer (mouse / touch). The focus cues - <see cref="FocusOutlineWidget"/>'s glow and
/// <see cref="SelectableScaleWidget"/>'s grow - only show while <see cref="Navigating"/>: with a mouse or a finger
/// the EventSystem still holds a selection (clicks select, screens pre-focus their default item), it just isn't drawn.
/// The mode flips on the first input of the other kind. Self-installed: no scene setup.
/// </summary>
public static class UiInputMode
{
    public static bool Navigating { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        Navigating = false;
        var go = new GameObject("UiInputMode") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Driver>();
    }

    private class Driver : MonoBehaviour
    {
        private void Update()
        {
            if (Navigating)
            {
                if (PointerUsed())
                    Navigating = false;
            }
            else if (NavigationUsed())
            {
                Navigating = true;
            }
        }

        private static bool NavigationUsed()
        {
            return Quantum.GamepadControls.NavigateActive || Quantum.GamepadControls.SubmitPressed
                || Quantum.GamepadControls.CancelPressed || Quantum.GamepadControls.TabPrevPressed
                || Quantum.GamepadControls.TabNextPressed;
        }

        private static bool PointerUsed()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0.5f || mouse.leftButton.wasPressedThisFrame
                                  || mouse.rightButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0f))
                return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }
    }
}
