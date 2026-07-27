namespace Quantum {
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
      Quantum.Input i = callback.PlayerSlot == 1 ? PollPlayerTwoInput() : PollPlayerOneInput();
      callback.SetInput(i, DeterministicInputFlags.Repeatable);
    }

    private Quantum.Input PollPlayerOneInput() {
      Quantum.Input i = new Quantum.Input();
      float x = UnityEngine.Input.GetAxis("Horizontal");
      float y = UnityEngine.Input.GetAxis("Vertical");
      bool shiftHeld = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
      bool jump = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Space);
      bool fire = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Mouse0);
      bool switchTarget = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Tab);
      bool skill2 = UnityEngine.Input.GetKey(UnityEngine.KeyCode.E);

      i.Direction = new FPVector2(x.ToFP(), y.ToFP());
      // Run and DashSkill intentionally share Shift: Run.IsDown keeps driving sprint continuously,
      // DashSkill.WasPressed triggers the dash on the tap edge - both read off the same held key.
      i.Run = shiftHeld;
      i.DashSkill = shiftHeld;
      i.Jump = jump;
      i.Fire = fire;
      i.SwitchTarget = switchTarget;
      i.HeroSkill = skill2;

      return i;
    }

    private Quantum.Input PollPlayerTwoInput() {
      Quantum.Input i = new Quantum.Input();
      float x = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftArrow) ? 1f : 0f);
      float y = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow) ? 1f : 0f);
      bool dash = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad0);
      bool fire = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad1);
      bool skill2 = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Keypad2);

      i.Direction = new FPVector2(x.ToFP(), y.ToFP());
      // Same Run/DashSkill sharing as player one, mapped to the dash key instead of Shift.
      i.Run = dash;
      i.DashSkill = dash;
      i.Fire = fire;
      i.HeroSkill = skill2;

      return i;
    }
  }
}
