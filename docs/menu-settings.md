# Menu Settings Popup

MenuScene `UiPopup` opened from the main menu's TopBar `SettingsButton`: SFX / Music / Voice sliders and a
Photon region dropdown. The in-match counterpart is `docs/in-match-settings.md`.

## Behavior

- **Sliders:** the same `AudioManager.SfxVolume` / `MusicVolume` / `VoiceVolume` persisted category
  multipliers as the in-match popup (see that doc) - one AudioManager, one set of prefs, so both popups
  always agree. Voice = `SoundGroup.Voice` (hero barks / announcer).
- **Region dropdown:** option 0 = **None (find best)**, then `PhotonRegionSettings.Regions` - only the regions
  whitelisted in the Photon dashboard (`eu;us;sa`); keep the two in sync. Persisted in pref `photon_region` (+ `LocalClientIdentity.PrefSuffix`), empty = None.
  - `MatchMakingConfig.Connect` calls `PhotonRegionSettings.ApplyTo(PhotonSettings)` before every connect:
    None -> `FixedRegion = null` and `BestRegionSummaryFromStorage = null` (fresh ping of all regions);
    otherwise `FixedRegion = <code>`. A reconnect still overrides it with the room's region
    (`MatchmakingExtensions.ConnectToRoomAsync`).
  - If the client is still connected to a Master server (not in a room) on a different region, `Connect`
    disconnects first (`NeedsReconnectFor`): `ConnectUsingSettingsAsync` is a no-op for an already-connected
    client, which used to make a region change silently not apply.
  - `MatchMakingConfig.OnConnectedToMaster` -> `RememberConnectedRegion(Client.CurrentRegion)`: under None,
    the region Photon picked is **saved as the selection**, so later connects go straight there. Choosing
    None again asks for a new search on the next connect. A manual pick is never overwritten.
  - `PhotonRegionSettings.Changed` refreshes an open popup (e.g. the auto-save landing while it's open).
  - Locked (non-interactable) while `Client.InRoom` (in a party) - a change only applies to the next connect.
- **Open:** `MenuSettingsPopup.Open()` -> `PopupManager.instance.AddPopupToQueue(this)`. Closes on dim click.

## Files

| File | Role |
| --- | --- |
| `Scripts/UI/Menu/Popups/MenuSettingsPopup.cs` | The popup. |
| `Scripts/PhotonRegionSettings.cs` | Region pref, region list, `ApplyTo`, `RememberConnectedRegion`. |
| `Scripts/MatchMakingConfig.cs` | Calls `ApplyTo` in `Connect`, `RememberConnectedRegion` in `OnConnectedToMaster`. |
| `Scripts/Audio/AudioManager.cs` | `SfxVolume` / `MusicVolume` / `VoiceVolume`. |
| `Editor/MenuSettingsPopupBuilder.cs` | `Tools > RiftRaiders > UI > Create Menu Settings Popup` (active scene only; reuses `InMatchSettingsPopupBuilder` helpers; adds a Button to `SettingsButton` if missing and wires it). |

## Current status

Built into `MenuScene` under `PopupManager` and saved; `TopBar/SettingsButton` got a `Button` wired to
`Open`. Compiles. Not yet verified in Play Mode (sliders, dropdown list sizing, best-region auto-save).
Plain placeholder UGUI - restyle it.

## Known simplifications

- Photon rooms are **per region**: party members on different regions can't find each other's room code.
  Nothing syncs region across a party - the hint text just warns about it.
- The region list is static and mirrors the dashboard whitelist (`eu;us;sa`) by hand.
- Region codes outside the list (saved from a future auto-pick) display as None but are kept.
