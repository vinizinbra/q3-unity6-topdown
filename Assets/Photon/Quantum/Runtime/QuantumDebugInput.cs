namespace Quantum {
  using ControlFreak2;
  using Photon.Deterministic;
  using UnityEngine;

  /// <summary>
  /// A Unity script that creates empty input for any Quantum game.
  /// </summary>
  public class QuantumDebugInput : MonoBehaviour {

    private void OnEnable() {
      QuantumCallback.Subscribe(this, (CallbackPollInput callback) => PollInput(callback));
    }

    /// <summary>
    /// Set keyboard-driven debug input for the polled local player slot.
    /// </summary>
    /// <param name="callback"></param>
    public void PollInput(CallbackPollInput callback) {
      // A bot slot (RuntimePlayer.IsBot - see docs/bots.md) has its Input synthesized inside the
      // simulation by BotInputSystem, which ignores whatever is polled here. Sending empty input
      // for it anyway keeps the two from looking like rival drivers of the same character - and
      // more practically, stops the human's own keys from being mirrored onto a bot slot by the
      // PlayerSlot ternary below (with three local players, slots 0 and 2 both fall through to
      // PollPlayerOneInput).
      // A menu/popup/window being open (UiInputGate) also sends empty input - the stick, Submit and clicks
      // that drive the UI must not move or fire the character behind it. Still polled every tick, so a held
      // key/stick is released in the simulation the moment the UI opens.
      Quantum.Input i;
      if (IsBotSlot(callback)) {
        i = default;
      } else {
        i = callback.PlayerSlot == 1 ? PollPlayerTwoInput() : PollPlayerOneInput();
        i = UiInputGate.FilterAfterUi(callback.PlayerSlot, i);
      }
      callback.SetInput(i, DeterministicInputFlags.Repeatable);
    }

    // Maps this poll's local PlayerSlot back to the PlayerRef occupying it (GetLocalPlayers/
    // GetLocalPlayerSlots are parallel arrays) so the slot's own RuntimePlayer can be read. Any
    // uncertainty - no game, no frame, player not added yet - reports "not a bot", so a real
    // player can never be silently muted by this.
    private static bool IsBotSlot(CallbackPollInput callback) {
      QuantumGame game = callback.Game;
      if (game == null) {
        return false;
      }

      Frame frame = game.Frames.Predicted;
      if (frame == null) {
        return false;
      }

      var localPlayers = game.GetLocalPlayers();
      var localSlots = game.GetLocalPlayerSlots();

      for (int i = 0; i < localPlayers.Count && i < localSlots.Count; i++) {
        if (localSlots[i] != callback.PlayerSlot) {
          continue;
        }

        RuntimePlayer playerData = frame.GetPlayerData(localPlayers[i]);
        return playerData != null && playerData.IsBot;
      }

      return false;
    }

    private Quantum.Input PollPlayerOneInput() {
      Quantum.Input i = new Quantum.Input();
      // Real gamepad hardware reads through GamepadControls (Input System, one binding set for every
      // pad - see there to re-point a button).
      // CF2Input is reserved for keyboard + on-screen mobile touch controls (Control Freak 2's own
      // Input Rig binds both to the same virtual "Horizontal"/"Vertical"/KeyCode targets), so a
      // touch build gets the same code path as desktop keyboard for free.
      float x = CF2Input.GetAxis("Horizontal")*2 ;
      float y = CF2Input.GetAxis("Vertical")*2;
      if(Mathf.Abs(x) < 0.1f && Mathf.Abs(y) < 0.1f) {
        Vector2 stick = GamepadControls.Move;
        x = stick.x;
        y = stick.y;
      }
      bool shiftHeld = GamepadControls.DashHeld || CF2Input.GetKey(UnityEngine.KeyCode.LeftShift) || CF2Input.GetKey(UnityEngine.KeyCode.RightShift);
      bool jump = CF2Input.GetKey(UnityEngine.KeyCode.Space);
      bool fire = UnityEngine.Input.GetMouseButton(0);
      bool switchTarget = GamepadControls.SwitchTargetHeld || CF2Input.GetKey(UnityEngine.KeyCode.Tab);
      bool ping = GamepadControls.PingHeld || UnityEngine.Input.GetMouseButton(2);
      bool skill2 = GamepadControls.SkillHeld || CF2Input.GetKey(UnityEngine.KeyCode.E);

      Vector2 worldDirection = ApplyCameraYaw(x, y);
      i.Direction = new FPVector2(worldDirection.x.ToFP(), worldDirection.y.ToFP());
      // Run and DashSkill intentionally share Shift: Run.IsDown keeps driving sprint continuously,
      // DashSkill.WasPressed triggers the dash on the tap edge - both read off the same held key.
      i.Run = shiftHeld;
      i.DashSkill = shiftHeld;
      i.Jump = jump;
      i.Fire = fire;
      i.SwitchTarget = switchTarget;
      i.Ping = ping;
      i.HeroSkill = skill2;

      return i;
    }

    private Quantum.Input PollPlayerTwoInput() {
      Quantum.Input i = new Quantum.Input();
      float x = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftArrow) ? 1f : 0f);
      float y = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow) ? 1f : 0f);
      bool dash = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad0);
      bool jump = UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl);
      bool fire = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad1);
      bool switchTarget = UnityEngine.Input.GetKey(UnityEngine.KeyCode.KeypadEnter);
      bool skill2 = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad2);

      Vector2 worldDirection = ApplyCameraYaw(x, y);
      i.Direction = new FPVector2(worldDirection.x.ToFP(), worldDirection.y.ToFP());
      // Same Run/DashSkill sharing as player one, mapped to the dash key instead of Shift.
      i.Run = dash;
      i.DashSkill = dash;
      i.Jump = jump;
      i.Fire = fire;
      i.SwitchTarget = switchTarget;
      i.HeroSkill = skill2;

      return i;
    }

    // Rotates the raw (Horizontal, Vertical) axis pair by the gameplay camera's current yaw before
    // it becomes Quantum.Input.Direction, so "up" always moves toward the top of the screen from
    // the camera's point of view instead of always meaning world +Z. Done here (Unity/view side,
    // per client) rather than in simulation code because only the resulting world-space vector is
    // what actually needs to be deterministic/replicated - each client reads its own local camera.
    // Uses Camera.main (tagged MainCamera on the gameplay Camera, see QuantumGameScene.unity)
    // rather than the project's own FollowCamera type - this file compiles in the Quantum.Unity
    // asmdef, which can't reference FollowCamera.cs (no asmdef of its own, so it's part of the
    // default Assembly-CSharp, which always builds after named assemblies like this one).
    // FollowCamera has no yaw today (fixed 45 deg pitch, see FollowCamera.cs), so this is a no-op
    // until the camera actually rotates - it's here so movement keeps feeling right once it does.
    private static Vector2 ApplyCameraYaw(float x, float y) {
      Camera mainCamera = Camera.main;
      Transform cameraTransform = mainCamera != null ? mainCamera.transform : null;
      if (cameraTransform == null) {
        return new Vector2(x, y);
      }

      Vector3 forward = cameraTransform.forward;
      forward.y = 0f;

      Vector3 right = cameraTransform.right;
      right.y = 0f;

      if (forward.sqrMagnitude < 0.0001f || right.sqrMagnitude < 0.0001f) {
        // Camera looking straight down (or up) - no meaningful yaw to project onto the ground plane.
        return new Vector2(x, y);
      }

      forward.Normalize();
      right.Normalize();

      Vector3 worldDirection = right * x + forward * y;
      return new Vector2(worldDirection.x, worldDirection.z);
    }
  }
}
