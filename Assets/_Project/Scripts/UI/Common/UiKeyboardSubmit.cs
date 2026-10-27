using Quantum;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Space and E press the focused UI element, like Enter / gamepad South. The EventSystem's InputSystemUIInputModule
/// uses the package's default UI actions, whose keyboard Submit is Enter only, so this sends the Submit itself.
/// E is also the gameplay Skill key: during a match the keys only submit while a popup / window owns input
/// (<see cref="UiInputGate.IsBlocked"/>), so a HUD button left selected by a click can't be pressed by a skill press.
/// Skipped while a text field is being edited (they are just characters there). Self-installed: no scene setup.
/// </summary>
public static class UiKeyboardSubmit
{
    /// <summary>Space / E submitted the focused element this frame (<see cref="UiPressDetector"/> treats it like Submit).</summary>
    public static bool PressedThisFrame
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !(keyboard.spaceKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
                return false;

            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null || SubmitToEditInputField.IsEditing(selected))
                return false;

            return QuantumRunner.Default == null || UiInputGate.IsBlocked;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject(nameof(UiKeyboardSubmit)) { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Driver>();
    }

    private class Driver : MonoBehaviour
    {
        private void Update()
        {
            if (!PressedThisFrame)
                return;

            EventSystem eventSystem = EventSystem.current;
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
        }
    }
}
