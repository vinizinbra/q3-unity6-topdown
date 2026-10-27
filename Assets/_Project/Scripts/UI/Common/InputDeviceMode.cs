using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Whether the player is on a gamepad or on keyboard + mouse, for device-aware button prompts (e.g. the Training
/// Zone ground glyphs, <see cref="TrainingInputPromptWidget"/>). Starts on gamepad when one is connected, then flips
/// on the first button press / stick push of the other kind. Not the same thing as <see cref="UiInputMode"/>
/// (navigation vs pointer): a keyboard player navigating a menu is Navigating but not <see cref="UsingGamepad"/>.
/// Self-installed: no scene setup.
/// </summary>
public static class InputDeviceMode
{
    public static bool UsingGamepad { get; private set; }

    /// <summary>Fired when <see cref="UsingGamepad"/> flips.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        UsingGamepad = Gamepad.current != null;
        var go = new GameObject("InputDeviceMode") { hideFlags = HideFlags.HideInHierarchy };
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Driver>();
    }

    private static void Set(bool usingGamepad)
    {
        if (UsingGamepad == usingGamepad)
            return;

        UsingGamepad = usingGamepad;
        Changed?.Invoke();
    }

    private class Driver : MonoBehaviour
    {
        private void Update()
        {
            if (UsingGamepad)
            {
                if (KeyboardOrMouseUsed())
                    Set(false);
            }
            else if (GamepadUsed())
            {
                Set(true);
            }
        }

        // Every Gamepad button, including the stick/D-pad directions and triggers, which are ButtonControls with
        // the default press point - so stick drift below it never flips the mode.
        private static bool GamepadUsed()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
                return false;

            foreach (InputControl control in pad.allControls)
            {
                if (control is ButtonControl button && button.wasPressedThisFrame)
                    return true;
            }

            return false;
        }

        private static bool KeyboardOrMouseUsed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                return true;

            Mouse mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
        }
    }
}
