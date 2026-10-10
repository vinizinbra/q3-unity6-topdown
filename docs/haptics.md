# Haptics

Phone vibration and gamepad rumble through **Nice Vibrations 4.1.2** (Lofelt), copied into
`Assets/3rd-party/NiceVibrations` from Pack n match without its `Define/` (Feel-only) and `OlderVersions/`
folders. Nothing outside `Scripts/Haptics/` references Lofelt.

## How a haptic gets triggered (pick one; never call Nice Vibrations directly)

1. **On a sound:** `SoundData.haptic`. `AudioManager.PlayInternal` fires it once the play has passed every
   gate (cooldown, distance cull, voice budget), so it only buzzes for a play you hear. Use it for moments that
   already have a sound. Only tag sounds that are about *you*: flat UI/global cues or `localPlayerOnly`. A spatial
   sound anyone can trigger would vibrate for everyone's.
2. **On a gameplay event:** `HapticsDirector` subscribes to a fixed set of Quantum events; which preset each
   plays (None = off) and its cooldown are on `Resources/HapticsConfig` (`HapticCue`). To add an event:
   one field on `HapticsConfig` + one `Subscribe` line in `HapticsDirector.Awake`.
3. **In code, as a last resort:** `Haptics.Play(HapticPreset)` / `Haptics.Play(HapticCue)`.

## Pieces

| File | Role |
| --- | --- |
| `Scripts/Haptics/Haptics.cs` | `HapticPreset` enum (None = 0), `HapticCue`, the static `Haptics` entry point: on/off pref (`haptics_enabled`, default on), per-cue cooldown, strength arbitration (a weaker preset never cuts a stronger one still playing). |
| `Scripts/Haptics/HapticsConfig.cs` | Per-event cues; `Resources/HapticsConfig.asset` (falls back to code defaults if missing). |
| `Scripts/Haptics/HapticsDirector.cs` | Self-installed (`RuntimeInitializeOnLoadMethod`, DontDestroyOnLoad, hidden): UI presses + gameplay events. Nothing to place in a scene. |
| `Scripts/UI/Common/HapticsSettingRowWidget.cs` | Settings row: Toggle ↔ `Haptics.Enabled`, ON/OFF readout. Plays Success when switched on. |

## Events (HapticsConfig defaults)

| Event | Who | Preset |
| --- | --- | --- |
| UI press / submit on an interactable click target | - | Selection |
| `EntityDamaged` (not Silent) | target local | Medium (0.25s cd) |
| `ShieldBroken` / `AccessoryBroken` | local | Heavy |
| `AccessoryBlocked` | local | Rigid |
| `PlayerDowned` / `PlayerKO` | local | Heavy / Failure |
| `PlayerRevived` | target or reviver local | Success |
| `SkillActionBeginExecuted` (incl. Dash) | local | Soft (0.1s cd) |
| `EntityDamaged` IsCritical | owner local | Light (0.12s cd) |
| `EntityDied` Elite | owner local | Heavy |
| `EntityDied` Boss | everyone | Heavy |
| `GroundbreakerSlammed` / `WallSlammed` | owner local | Rigid |
| `GameStateChanged` → Upgrade / Boss / Victory / RunFailed | everyone | Success / Heavy / Success / Failure (ignored when returning from Upgrade) |

"Local" = `MyLocalPlayer.IsLocalEntity` (every couch slot; never bots).

**UI detection:** on a touch/mouse press, raycast the EventSystem and take the topmost hit's
`IPointerClickHandler`; on the UI module's submit action, the selected object's `ISubmitHandler` **as cached in the previous frame's LateUpdate** - Submit clicks on the press frame inside `EventSystem.Update`, which can run first and has already disabled the target (an upgrade card deactivates the moment it is picked), so reading the selection live missed gamepad A on upgrade cards. Must be an
interactable `Selectable`, or a handler with none (custom cards). CF2's touch panel only implements pointer
down/up, so the gameplay joystick/buttons never trigger it. Every touch is checked, not just the primary one.

## Platforms

- iOS: native Taptic presets (`PlayPreset`).
- Android / gamepad: Nice Vibrations' single-impact presets are too short/weak to feel there (Selection = 40 ms
  at 47 %, Light = 20 ms at 1 %; a pad motor doesn't spin up), which made every UI tap and crit silent. So
  Selection/Light/Soft/Medium/Rigid/Heavy play a tuned `PlayConstant` pulse instead (the `*Pulse` fields on `HapticsConfig`,
  ~50 ms minimum); Success/Failure/Warning keep their presets (`clipLevel` is reset to 1 first, since
  `PlayConstant` leaves its amplitude there).
- Desktop / Editor: gamepad rumble only (Input System), mobile vibration is not simulated.

## Current status (2026-10-09)

Compiles; `HapticsDirector` installs in Play Mode with no errors. The Haptics row exists in both settings
popups (MenuScene: added but left **unsaved** with the scene's other pending changes; GrasslandOutpostGameScene: saved),
built from a copy of the Quality row (unpacked, not a prefab instance). **Not yet felt on a device or with a pad.**
No SoundData has a haptic assigned yet.
