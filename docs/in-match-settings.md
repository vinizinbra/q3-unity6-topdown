# In-Match Settings Popup

A pause-menu style `UiPopup` opened mid-match: SFX slider, Music slider, Restart (offline only), Disconnect.

## Behavior

- **Open:** Escape (desktop only - on mobile Escape is Android Back, which toggles the Hero Info popup) or gamepad Start (`OpenInMatchSettings`: `joystick button 10` on Android pads, alt `7`), via `InMatchPopupManager.Update`, or any button wired to `InMatchPopupManager.OpenSettings`.
  Escape only opens it over an *empty* popup stage and closes it again when it is the current popup - it
  never dismisses or stacks over another popup (a tutorial popup's `Close` also unpauses the sim, so Escape
  must not be a way to skip one).
- **Sliders:** drive `AudioManager.MusicVolume` / `AudioManager.SfxVolume` - two persisted *category
  multipliers* applied on top of the per-`SoundGroup` buses in `ResolveBusVolume`. Music is its own
  category; every other group (Sfx/Ambience/Ui/Voice/...) counts as SFX. They are multipliers rather than
  driving the group buses so the authored balance between groups is never overwritten. Prefs:
  `audio_volume_category_music` / `audio_volume_category_sfx` (cleared by AudioManager's *Reset Saved Volumes*).
- **Disconnect:** `MatchMakingConfig.LeaveMatch()` - already routes offline vs online.
- **Restart (offline only):** button hidden unless `GameManager.isPlayingOffline` (re-evaluated every `Show`).
  `MatchMakingConfig.RestartOfflineMatch()` clears the duplicate-start guard, `QuantumRunner.ShutdownAll()`
  (the Quantum SDK unloads the gameplay scene), then `StartOfflineRunner()` - which shows `LoadingWindow`,
  waits for the old scene to unload and the old runner to deregister, and starts a fresh session with a new
  seed. Online runs are never restartable: they are a shared deterministic simulation.

## Files

| File | Role |
| --- | --- |
| `Scripts/UI/InGame/InMatchSettingsPopup.cs` | The popup. |
| `Scripts/UI/InGame/InMatchPopupManager.cs` | `OpenSettings()` + Escape toggle. |
| `Scripts/Audio/AudioManager.cs` | `MusicVolume` / `SfxVolume`. |
| `Scripts/MatchMakingConfig.cs` | `RestartOfflineMatch()`. |
| `Editor/InMatchSettingsPopupBuilder.cs` | `Tools > RiftRaiders > UI > Create In-Match Settings Popup`. |

## Editor authoring needed

Done: the builder has been run in `GrasslandOutpostGameScene` (popup is under `PopupManager`, all fields
wired, scene saved). Still to do: restyle it, optionally wire a HUD gear button to
`GameplayUiController.OpenSettings`, and verify in Play Mode (sliders persist, Disconnect, offline Restart -
none of it has been run yet; it only compiles).

## Known simplifications

- The sim is **not** paused while the popup is open (solo included). A `SetTutorialPauseCommand`-style pause
  for offline/solo would be the follow-up.
- No confirmation step on Disconnect/Restart.
- The SFX slider gives no audible preview on release.
