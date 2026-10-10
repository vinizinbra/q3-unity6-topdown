namespace Quantum {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// "Is a menu/popup/window open?" - when it is, <see cref="QuantumDebugInput"/> sends EMPTY input to the
  /// simulation instead of what the player is pressing, so moving the stick, pressing Submit or clicking a
  /// popup button doesn't also move or fire the character behind it. Commands (picking a card, ...) are not
  /// input and are unaffected.
  /// </summary>
  /// <remarks>
  /// Lives in the Quantum assembly because the input poll does; the UI (Assembly-CSharp) feeds it through
  /// <see cref="Register"/> - see UiPopup (registers itself) and UiInputBlockerWidget (put on a window).
  /// Sources are polled when input is polled, so a source never has to be unregistered: one whose owner was
  /// destroyed is dropped, and one whose owner is disabled simply doesn't block.
  /// </remarks>
  public static class UiInputGate {
    private struct Source {
      public Behaviour Owner;
      public Func<bool> IsBlocking;
    }

    private static readonly List<Source> Sources = new List<Source>();

    /// <summary>True while any registered source is blocking.</summary>
    public static bool IsBlocked {
      get {
        for (int i = Sources.Count - 1; i >= 0; i--) {
          Source source = Sources[i];
          if (source.Owner == null) {
            Sources.RemoveAt(i);
            continue;
          }

          if (source.Owner.isActiveAndEnabled && source.IsBlocking()) {
            return true;
          }
        }

        return false;
      }
    }

    /// <summary>
    /// Blocks gameplay input while <paramref name="owner"/> is active and enabled and
    /// <paramref name="isBlocking"/> returns true. Call once (e.g. in Awake).
    /// </summary>
    public static void Register(Behaviour owner, Func<bool> isBlocking) {
      if (owner == null || isBlocking == null) {
        return;
      }

      Sources.Add(new Source { Owner = owner, IsBlocking = isBlocking });
    }


    // Buttons held while a UI was open (or still held right after it closed), per local slot. The press that
    // closes a window (Submit/A, a click) is usually still down on the tick the gate lets input through, which
    // the sim would read as a fresh Dash/Fire. A latched button stays muted until it is seen released once.
    [Flags]
    private enum Latch { None = 0, Run = 1, Dash = 2, Jump = 4, Fire = 8, Switch = 16, Ping = 32, Skill = 64 }

    private static readonly Latch[] Latched = new Latch[8];

    /// <summary>
    /// Applies the gate to the freshly polled <paramref name="raw"/> input: empty while a UI is open, and
    /// afterwards only the buttons that were still held when it closed stay muted until released.
    /// Movement is never muted once the UI is gone.
    /// </summary>
    public static Input FilterAfterUi(int slot, Input raw) {
      slot = Mathf.Clamp(slot, 0, Latched.Length - 1);

      Latch down = Latch.None;
      if (raw.Run) down |= Latch.Run;
      if (raw.DashSkill) down |= Latch.Dash;
      if (raw.Jump) down |= Latch.Jump;
      if (raw.Fire) down |= Latch.Fire;
      if (raw.SwitchTarget) down |= Latch.Switch;
      if (raw.Ping) down |= Latch.Ping;
      if (raw.HeroSkill) down |= Latch.Skill;

      if (IsBlocked) {
        Latched[slot] = down;
        return default;
      }

      // Drop latches for buttons that were released; keep the ones still held.
      Latch latched = Latched[slot] & down;
      Latched[slot] = latched;
      if (latched == Latch.None) {
        return raw;
      }

      if ((latched & Latch.Run) != 0) raw.Run = false;
      if ((latched & Latch.Dash) != 0) raw.DashSkill = false;
      if ((latched & Latch.Jump) != 0) raw.Jump = false;
      if ((latched & Latch.Fire) != 0) raw.Fire = false;
      if ((latched & Latch.Switch) != 0) raw.SwitchTarget = false;
      if ((latched & Latch.Ping) != 0) raw.Ping = false;
      if ((latched & Latch.Skill) != 0) raw.HeroSkill = false;
      return raw;
    }

    // Domain reload is off in some setups: don't carry sources from a previous play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() {
      Sources.Clear();
      Array.Clear(Latched, 0, Latched.Length);
    }
  }
}
