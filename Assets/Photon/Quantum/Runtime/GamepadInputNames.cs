namespace Quantum {
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.SceneManagement;

  /// <summary>
  /// Resolves a named Input Manager gamepad entry (ProjectSettings/InputManager.asset) to the set
  /// matching the pad this build actually talks to.
  /// </summary>
  /// <remarks>
  /// The default names (GamepadDash, OpenInMatchSettings, ...) are tuned for the MOGA Pro 2 on
  /// Android. The macOS Editor numbers an Xbox 360 pad completely differently (A = button 1,
  /// X = 4, Start = 12, Select = 16, R2 = axis 5), so there it reads a parallel "Editor"-prefixed
  /// copy of every entry instead. The Windows Editor isn't redirected - XInput numbering already
  /// matches the default set. Every name passed to <see cref="Get"/> needs its Editor twin in the
  /// Input Manager, or GetButton/GetAxis throws there.
  /// </remarks>
  public static class GamepadInputNames {
#if UNITY_EDITOR_OSX
    private const string Prefix = "Editor";
#else
    private const string Prefix = "";
#endif

    public static string Get(string name) => Prefix + name;

#if UNITY_EDITOR_OSX
    // The uGUI StandaloneInputModules are authored in each scene with the default Horizontal/
    // Vertical/Submit/Cancel - re-pointed here on every scene load instead of per scene. A separate
    // set rather than extra bindings on the defaults: the 360's A is button 1, which is the MOGA's
    // Cancel, and the default Horizontal/Vertical are pinned to joystick 1 (joyNum 1) while the
    // Editor copies read any joystick.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RedirectUiInputModules() {
      RedirectLoadedModules();
      SceneManager.sceneLoaded -= OnSceneLoaded;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RedirectLoadedModules();

    private static void RedirectLoadedModules() {
      foreach (StandaloneInputModule module in Object.FindObjectsByType<StandaloneInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
        module.horizontalAxis = Get("Horizontal");
        module.verticalAxis = Get("Vertical");
        module.submitButton = Get("Submit");
        module.cancelButton = Get("Cancel");
      }
    }
#endif
  }
}
