# Smart Ping

One ping button, no ping menu. `PingSystem` resolves *what* was pinged from context and raises a
`synced` `PingPlaced` event; the HUD feed and the minimap radar are view-side listeners.

## Input
Gamepad **Y** (`GamepadControls.PingHeld`), keyboard **middle mouse** (P1 only for now). Input.qtn has
`button Ping`; the sim uses `WasPressed`. SwitchTarget moved to **R1**; the old R1 Fire binding was removed.

## Resolution (first match wins) - `Systems/Player/PingSystem.cs`
| Pinged | `PingKind` | Message |
|---|---|---|
| Own Downed state | ReviveMe | Revive me! |
| Elite: `Aim.LockedTarget`/`Target`, or nearest in front within `EliteViewRadius` | Elite | Focus Elite |
| Ping-able `Interactable` near the player or the aim point | Store/Blacksmith/Shrine/Rift | (see `PingPresentation.GetMessage`) |
| TeamChallenge Available/WaitingForTeam | TeamChallengeFound | Found Team Challenge |
| TeamChallenge Starting/ChallengeActive | TeamChallengeStart | Let's start Team Challenge! |
| TraversalChallenge | Traversal | Help here! |
| Empty ground ahead | Ground | Look here |
| 3 Ground pings in 3s | StickTogether (marks the pinger's own position) | Stick together! |

Works in Breathing and Survival (not tied to `PoiAvailability`). KO and bots never ping.
Cooldowns (per player): 0.5s general, 10s on StickTogether. `PlayerPing` is added lazily via `AddOrGet`.

## View
- `PingFeedWidget` (`UI/InGame/Hud`) - self-building chat feed, last 5 lines, 8s life, built by
  `GameplayUiController.QStart` via `PingFeedWidget.Ensure`. `PingPresentation` holds text + per-slot colour.
- `MinimapWidget` - `OnPingPlaced`/`UpdatePingRadars`: pulsing ring + dot per player (one at a time,
  8s), counter-scaled like other overlays, so it shows on the corner and the expanded map. Runs after
  `CenterOnLocalPlayer`; a ping outside the masked viewport is pinned to its edge (same
  `ClampInsideViewport`/`pinActivePoisToEdge` as POI icons) and shown as a steady dot.
- `PingWorldRippleWidget` - 3D ground ripple at the ping position (two staggered rings + dot, flat
  SpriteRenderers, player colour, 8s, one per player). Ring/dot sprite shared via `PingPresentation.GetSprite`.
- Sounds: `Assets/_Project/Audio/Ping/*.wav` are synthesised (FM bell / marimba / pad + convolution
  reverb, 48kHz stereo); swap any clip in its `SoundData`.

## Known simplifications / TODO
- **Hurry up!** (ping a player still choosing in Level-Up/ChooseWindow/Store) is NOT built: the Level-Up
  pause is in `GameplaySystemGroup`, so `PingSystem` doesn't run then - needs a Command.
- World ripple is a flat ground sprite (can hide behind tall walls); it doesn't follow a pinged Elite.
- Per-hero colour is a placeholder palette indexed by player slot.
- Tuning values are constants at the top of `PingSystem`; Elite "in view" is a flat radius, not the camera.
- Keyboard P2 / touch have no ping binding.
