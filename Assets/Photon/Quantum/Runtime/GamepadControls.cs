namespace Quantum {
  using UnityEngine;
  using UnityEngine.InputSystem;

  /// <summary>
  /// The single place where physical gamepad/keyboard controls are bound to game actions, on the
  /// Input System package. Replaces the old per-device named Input Manager entries
  /// (GamepadDash, EditorGamepadDash, ...), which needed a duplicate set for every pad because the
  /// legacy joystick numbering differs per device and OS.
  /// </summary>
  /// <remarks>
  /// Bindings target the layout-level <c>&lt;Gamepad&gt;</c> controls (buttonSouth/West/North,
  /// start/select, shoulders, triggers), which Unity maps for DualShock/Xbox/MOGA/MFi pads on every
  /// platform, so one binding set covers all of them. To move an action to another button, change its
  /// binding below. Keyboard movement/fire and the touch rig still go through Control Freak 2's
  /// legacy <c>Input</c> path (see QuantumDebugInput) - only gamepad + menu navigation live here.
  /// </remarks>
  public static class GamepadControls {
    private static InputActionMap _map;

    /// <summary>Left stick / D-pad as a gameplay move vector (up = +y).</summary>
    public static Vector2 Move => Map.FindAction("Move").ReadValue<Vector2>();
    public static bool DashHeld => Map.FindAction("Dash").IsPressed();
    public static bool SkillHeld => Map.FindAction("Skill").IsPressed();
    public static bool SwitchTargetHeld => Map.FindAction("SwitchTarget").IsPressed();
    /// <summary>Y - held; the sim edge-detects it into a ping.</summary>
    public static bool PingHeld => Map.FindAction("Ping").IsPressed();

    /// <summary>Start - toggles the in-match settings popup.</summary>
    public static bool OpenSettingsPressed => Map.FindAction("OpenSettings").WasPressedThisFrame();
    /// <summary>L2 (or Select/Share) - held to show the Hero Info popup.</summary>
    public static bool OpenHeroInfoHeld => Map.FindAction("OpenHeroInfo").IsPressed();
    /// <summary>Right trigger - toggles the expanded minimap (rising edge past the press point).</summary>
    public static bool ToggleMapPressed => Map.FindAction("ToggleMap").WasPressedThisFrame();

    // Menu navigation (uGUI focus itself is driven by the InputSystemUIInputModule's default actions;
    // these are for the project's own MenuNavigationController).
    public static bool NavigateActive => Map.FindAction("Navigate").ReadValue<Vector2>() != Vector2.zero;
    public static bool SubmitPressed => Map.FindAction("Submit").WasPressedThisFrame();
    public static bool CancelPressed => Map.FindAction("Cancel").WasPressedThisFrame();
    public static bool TabPrevPressed => Map.FindAction("TabPrev").WasPressedThisFrame();
    public static bool TabNextPressed => Map.FindAction("TabNext").WasPressedThisFrame();

    // Also re-arms the actions when Enter Play Mode runs without a domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
      _map?.Dispose();
      _map = null;
    }

    private static InputActionMap Map {
      get {
        if (_map == null)
          Build();
        return _map;
      }
    }

    private static void Build() {
      _map = new InputActionMap("RiftRaiders");

      InputAction move = _map.AddAction("Move", InputActionType.Value);
      move.AddBinding("<Gamepad>/leftStick");
      move.AddBinding("<Gamepad>/dpad");

      InputAction navigate = _map.AddAction("Navigate", InputActionType.Value);
      navigate.AddBinding("<Gamepad>/leftStick");
      navigate.AddBinding("<Gamepad>/dpad");
      AddKeys(navigate, "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow");
      AddKeys(navigate, "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");

      Button("Dash", "<Gamepad>/buttonSouth");
      Button("Skill", "<Gamepad>/buttonWest");
      Button("SwitchTarget", "<Gamepad>/rightShoulder");
      Button("Ping", "<Gamepad>/buttonNorth");

      Button("OpenSettings", "<Gamepad>/start");
      Button("OpenHeroInfo", "<Gamepad>/leftTrigger", "<Gamepad>/select");
      Button("ToggleMap", "<Gamepad>/rightTrigger");

      Button("Submit", "<Gamepad>/buttonSouth", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
      Button("Cancel", "<Gamepad>/buttonEast", "<Keyboard>/escape");
      Button("TabPrev", "<Gamepad>/leftShoulder", "<Keyboard>/leftBracket");
      Button("TabNext", "<Gamepad>/rightShoulder", "<Keyboard>/rightBracket");

      _map.Enable();
    }

    private static void Button(string name, params string[] paths) {
      InputAction action = _map.AddAction(name, InputActionType.Button);
      foreach (string path in paths)
        action.AddBinding(path);
    }

    private static void AddKeys(InputAction action, string up, string down, string left, string right) {
      action.AddCompositeBinding("2DVector")
        .With("Up", up)
        .With("Down", down)
        .With("Left", left)
        .With("Right", right);
    }
  }
}
