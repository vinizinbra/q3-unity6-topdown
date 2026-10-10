# Input (Input System + Control Freak 2)

**Active Input Handling = Both, permanently.** Control Freak 2 (touch rig, virtual sticks, keyboard via
`CF2Input`) only reads the legacy `UnityEngine.Input` and has no Input System support; Input System
*only* mode would throw in it. Do not switch to "Input System Package (New)" without replacing CF2.

## Who reads what

| Source | Path |
|---|---|
| Gamepad (any: DualShock 4, Xbox, MOGA, MFi) | `Quantum.GamepadControls` (Input System, `<Gamepad>` layout bindings) |
| Menu/uGUI focus, Submit/Cancel | `InputSystemUIInputModule` on each scene's EventSystem (package default actions) + `GamepadControls` for `MenuNavigationController` |
| Keyboard + mobile touch (gameplay) | `CF2Input` (legacy), see `QuantumDebugInput.PollPlayerOneInput` |
| Player 2 keyboard, mouse fire, Tab/Escape/M hotkeys | legacy `UnityEngine.Input` (still enabled by Both) |

Quantum side: `Quantum.Unity.asmdef` already references `Unity.InputSystem` and auto-defines
`QUANTUM_ENABLE_INPUTSYSTEM`; polling stays in the view (`QuantumDebugInput`), the sim stays deterministic.

## Gamepad bindings (edit `GamepadControls.Build`)

Dash = South (✕/A), Skill = West (□/X), Ping = North (△/Y), SwitchTarget = R1 (no Fire binding), Move = left stick/D-pad,
Settings = Start/Options, HeroInfo (held) = L2 (or Select/Share), Minimap toggle = R2, Cancel = East (○/B),
Submit = South, Tabs = L1/R1 (also `[` `]`).

Why: the legacy Input Manager numbers joystick buttons per device and OS (D3 on iOS reports DualShock 4
as buttons 10/11/13/14/15), which forced a parallel named-entry set per pad (`Editor*` for the macOS Xbox
360). One layout-level binding set replaces all of them; those entries were removed from InputManager.asset.

## Known simplifications
- Android Back (= Escape; also a MOGA's Select) still toggles HeroInfo/handled via legacy `KeyCode.Escape`
  in `HeroInfoPopupWidget` / `InMatchPopupManager`.
- Third-party fixes: `StompyRobot.SRDebugger.asmdef` references `Unity.InputSystem`; `PinEntryControl` /
  `KeyboardShortcutListenerService` guard `Keyboard.current == null` (phones).
- `GamepadHardwareTester` (CF2 debug overlay) and CF2's `GamepadInputModule` still read legacy joystick.
