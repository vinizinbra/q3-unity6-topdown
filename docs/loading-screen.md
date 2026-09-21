# Loading / Generating Level screen

A full-screen screen that covers the whole match start - from the moment the run is actually starting
until the local hero is standing in the world - then fades and hands off to `InMatchWindow`.

## Why it exists

`MatchMakingConfig.StartRunner` used to call `ShowWindow<InMatchWindow>()` the moment `AddPlayer`
returned. `InMatchWindow.Show()` disables the whole menu Canvas, so that call is what *reveals the
gameplay scene* - and at that point nothing is ready yet. Three things still have to finish:

1. the Quantum session starts and simulates its first tick,
2. `LevelGenerationSystem` places the level - deliberately spread over many ticks
   (`LevelConfig.ChunksPerGenerationTick`) so it doesn't hang the client, see that system's own
   comment, and
3. `PlayerSpawnUtility.IsReadyToSpawn` clears its 1s settle delay, so newly-created chunk colliders
   are registered in the physics broadphase before anyone with gravity/KCC drops in.

Until all three land the player is looking at a half-built level with no character in it, which reads
as the game being stuck. `LoadingWindow` owns that whole window instead, and `InMatchWindow` is only
shown once there is genuinely something to look at.

## Why it lives in the MENU, not in QuantumGameScene

The first version of this was a `LoadingScreenWidget` on its own Canvas inside `QuantumGameScene`.
That was the wrong home:

- **It can only cover the tail of the wait.** `SessionRunner.StartAsync` is what additively loads
  `QuantumGameScene`, so a screen living in that scene doesn't exist for the first (and slowest) part
  of the start. The menu Canvas, by contrast, is already up and already covering the screen.
- **It has to fight the scene it lives in.** The gameplay HUD Canvas sorts at 11, menu Canvases at 0.
- **It duplicated a transition the menu already owns.** The menu's `WindowManager` was already the one
  thing driving `MainMenuWindow -> ConnectingWindow -> InMatchWindow`; the loading screen belongs in
  that chain, not beside it.

So it is a real `UiWindow` under `MainMenuTab`'s own `WindowManager`, and the chain is now
`MainMenuWindow -> ConnectingWindow -> LoadingWindow -> InMatchWindow`, one continuous overlay with
no gap where an unfinished level is visible.

The one thing it borrows from the old design: **its own nested Canvas with `Override Sorting` on,
`sortingOrder` 999**. Without it the gameplay HUD (11) would draw over a menu window (0) the instant
`QuantumGameScene` loads. No `CanvasScaler` - a nested Canvas inherits its parent's scale, which is
what keeps it authored against the same reference resolution as every other menu window.

`WindowManager.ShowWindow<T>()` hiding every other window is not a hazard here (unlike for Cursed
Rift's own screen, see `docs/choice-window-refactor.md`): during a match start nothing else *should*
be shown, and being pre-empted is the correct behavior - the `StartRunner` failure path shows
`MainMenuWindow`, and `MatchMakingConfig.OnDisconnected` shows an `AlertPopup`.

## Files

| File | Role |
| --- | --- |
| `Assets/_Project/Scripts/UI/Menu/LoadingWindow.cs` | The whole runtime feature - stage resolution, progress, fade, hand-off. |
| `Assets/_Project/Editor/LoadingWindowBuilder.cs` | `Tools > RiftRaiders > Create Loading Window` - builds and wires the hierarchy into the menu scene. |

**No simulation change at all** - no `.qtn` edit, no new system, no codegen dependency. Everything it
reads already exists on `Global` (`Chunk.qtn`'s own comment on `LevelGenCursor`/`LevelGenTotal`
already named a "Generating level..." screen as their intended consumer).

Two lines changed in `MatchMakingConfig.StartRunner`: the `ShowWindow<ConnectingWindow>()` at the top
became `ShowWindow<LoadingWindow>()` (so the screen also covers `SessionRunner.StartAsync`, i.e. the
gameplay scene load itself), and the `ShowWindow<InMatchWindow>()` after `AddPlayer` was deleted -
that transition is now `LoadingWindow`'s own call. `ConnectingWindow` is untouched and still owns the
connect/room phase from `MainMenuWindow`; the callbacks it stopped registering during the start
window only ever raised alerts that `OnDisconnected` and `StartRunner`'s own `catch` already raise.

## Stages and progress

The bar is split into three bands, and is monotonic by construction (`ApplyProgress` only ever eases
upward) - a loading bar that goes backwards reads as a bug even when the numbers behind it are honest.

| Stage | Band | Source |
| --- | --- | --- |
| `CONNECTING` | 0 → 0.15 | No predicted frame yet (session starting, gameplay scene loading). Crawls. |
| `GENERATING LEVEL` | 0.15 → 0.85 | **Real**: `Global.LevelGenCursor / Global.LevelGenTotal`. Crawls only until `LevelGenTotal` is published on the first generation tick. |
| `ENTERING THE RIFT` | 0.85 → 1 | `PlayerSpawnUtility.IsReadyToSpawn`. Crawls until it's true. |

Crawling stages advance on their own accumulator rather than on the displayed value, so entering a
stage can never rewind the bar. Every stage change also logs one `LogHelper` line, so a genuine hang
is diagnosable from the log rather than from squinting at a bar that stopped moving.

## Hand-off condition

`MyLocalPlayer.Instance.AnyLocalPlayerSetup` - a local hero that exists **and** has registered its
view (`MyLocalPlayer.Register` runs off `CharView`), so this is true only once there is genuinely
something on screen to look at, not merely once the entity was created in the simulation. A client
with no local player at all (spectator) falls back to `PlayerSpawnUtility.IsReadyToSpawn`.

Then: **fade this window out first, then `ShowWindow<InMatchWindow>()`** - in that order, because
`InMatchWindow.Show` disables the menu Canvas, which would cut the fade off mid-way. Fading first
reveals the world while the menu is still up, so the Canvas goes down on an already-transparent
screen.

Whatever sits *behind* this window fades with it, via `fadeWithScreen` - an array of objects outside
this window's own hierarchy (typically the menu background, which is a separate object, not a child of
the window). One tween value drives this window's `CanvasGroup` and every entry together, so the
screen and its background can never end up at different alphas, and a `CanvasGroup` is added
automatically to anything in the array that doesn't have one - a plain background `Image` can be
dropped in as-is. Without this, the fade reveals the *main menu*, not the game.

Restoring those alphas is deliberately asymmetric: the hand-off's own `Hide` leaves them at 0 (the
whole point is that the world shows through, and this screen can't assume the background is about to
be hidden along with the menu Canvas - it may well be an object that isn't), while every OTHER hide
restores them to 1, so a pre-empted load or a later return to the menu never leaves it invisible. The
restore is gated on this screen having actually faded, since `WindowManager.ShowWindow` calls `Hide`
on every window that isn't the one being shown - otherwise ordinary menu navigation would write alpha
1 over a background this screen never touched.

Two guards either side of it:

- `minimumDisplayDuration` (1s) - a fast local start doesn't flash the screen for two frames.
- `maximumDisplayDuration` (45s) - hands off anyway, with a warning naming the stage it was stuck in,
  so a bad join can never trap a player behind a screen they can't dismiss. Set 0 to disable.

`Show()` only resets progress when the window was actually hidden, so a redundant
`ShowWindow<LoadingWindow>()` for a start already in flight can't rewind the bar or restart the
failsafe. `Hide()` stops the fade tween, so a window that pre-empts this one (the failure path's
`MainMenuWindow`) can never be replaced by a late `InMatchWindow` from a tween callback.

## Editor authoring

Run **`Tools > RiftRaiders > Create Loading Window`** with the menu scene open (`MenuScene`, the one
with `MainMenuTab`), then save the scene. It parents `LoadingWindow` under that tab's `WindowManager`
- which discovers its windows via `GetComponentsInChildren` at `Awake`, so parenting *is* the whole
registration step, there's no list to also remember to update - with its own override-sorting Canvas,
a `CanvasGroup`, a backdrop, title, stage label, progress bar, percent and tip line, wires every
serialized field, and leaves it deactivated (windows start hidden here). Re-running it selects the
existing window rather than building a second one.

Everything it creates is plain UGUI - restyle it freely afterwards, nothing reads back what it
authored. Two arrays are left for you to fill by hand, since both are scene-specific: `Fade With
Screen` (the menu background object(s) that must fade out with this screen - without them the fade
reveals the main menu rather than the game) and `Tips` (a rotating hint line; empty hides it).

## Current status (2026-08-26)

Code complete, compiles against existing types only. Nothing in the menu scene yet - the builder has
not been run. Not yet verified in-Editor.

One leftover: `Assets/gamesceneBackup.unity` contains a `LoadingScreen` GameObject built by the old
`LoadingScreenBuilder` (the builder was run with that backup scene open rather than
`QuantumGameScene`, which is why the old screen never appeared in a real match). Both old scripts are
deleted, so that object is now a missing-script reference in that backup scene only - delete the
GameObject if that scene is ever opened again.

---

# Generic transition loading screen (`SceneLoader` / `LoadingScreen`)

A SECOND, unrelated screen from the match-start `LoadingWindow` above. That one covers a Quantum session
starting inside the already-loaded menu and reads real level-generation progress; it stays in the menu
on purpose. This one covers the transitions the game owns outright, where no session exists:

```
PachaSplash -> Intro -> [generic loading] -> Menu -> (match start: LoadingWindow) -> Game
Game -> leave -> [generic loading] -> Menu
```

## Why a self-instantiated, persistent prefab

- **Not in any scene.** A screen living in a scene can't cover that scene's own unload, and would need
  placing in every scene that transitions. `LoadingScreen.Instance` Instantiates
  `Resources/LoadingScreen` on first use and marks it `DontDestroyOnLoad`, so it survives the swap it
  covers and works from any scene - including pressing Play from an arbitrary scene in the Editor.
- **Its own root Canvas, sortingOrder 32000**, above every HUD/menu/popup canvas (incl. `LoadingWindow`'s
  999). No `EventSystem` and no camera inside it (the scenes own those); it blocks input through its
  `CanvasGroup`/raycast-target background instead.
- **Saved inactive**, so it costs nothing while idle; `Activate()` turns it on before its coroutine
  starts (a coroutine can't start on an inactive object).

## API - callers only ever use `SceneLoader`

| Call | Use |
| --- | --- |
| `SceneLoader.Load("MenuScene")` | Single-scene load behind the screen. Fade in -> `LoadSceneAsync` with `allowSceneActivation = false` -> swap only once the scene is ready **and** the minimum has elapsed -> one settle frame -> fade out. |
| `SceneLoader.Cover(action, holdUntil)` | For a transition with **no scene load**: fade in, run `action` on the first fully covered frame - so the screen is always up BEFORE the teardown starts - hold to the minimum **and** until `holdUntil()` is true (optional), fade out. This is what "leave match -> menu" needs, because the menu scene never unloads. `holdUntil` covers work the action only *starts* (Quantum's async scene unload); it is bounded by `LoadingScreen.maximumHoldDuration` (10s) so a stuck condition can't trap the player. |
| `SceneLoader.IsBusy` | Static, never spawns the prefab. Debounces a second Leave click and tells an already-covered disconnect from one that still needs its own cover. |

Both take an optional per-call `minimumDuration`; the default is **2s**, measured from the start of the
fade-in (`LoadingScreen.minimumDuration`). If the prefab is missing or a scene name isn't in Build
Settings the facade falls back to a plain unmasked load / runs the action directly and logs - a bad
setup can never trap the player behind the screen. A second request while busy is ignored (scene load)
or run directly (cover).

## Files

| File | Role |
| --- | --- |
| `Assets/_Project/Scripts/UI/Common/LoadingScreen.cs` | The component + lazy singleton, both routines, optional progress bar/label. |
| `Assets/_Project/Scripts/UI/Common/SceneLoader.cs` | Static facade with the fallbacks. |
| `Assets/Resources/LoadingScreen.prefab` | The visuals - built from `Assets/LoadingCanvas.unity`. Restyle freely; only the root `LoadingScreen` + `CanvasGroup` matter. |

`IntroSequence` now hands off to the menu through `SceneLoader.Load`.

## Leaving a match (Game -> Menu)

Wired in `MatchMakingConfig`, and the ORDER is the point: **the screen goes up first, then the teardown
starts, and it lifts only once the runner is gone and the gameplay scene has finished unloading**
(`IsMatchTeardownComplete`: `QuantumRunner.Default == null` and `GameManager.GameplaySceneName` not
loaded - Quantum's `QuantumGame.Dispose` starts the map-unload coroutine itself).

| Path | What happens |
| --- | --- |
| `LeaveMatch()` (InMatchWindow / GameplayUiController Leave) | `SceneLoader.Cover(PerformLeaveMatch, IsMatchTeardownComplete)`. `PerformLeaveMatch` is the old body verbatim - offline shuts the runner down and shows `MainMenuWindow`; online calls `Client.Disconnect()`, whose `OnDisconnected` then runs *under* the cover. A second click while busy is ignored. |
| `OnDisconnected` (eviction, timeout, plugin disconnect) | If a match is on screen and no cover is up, it covers itself first, then runs `ReturnToMenuAfterDisconnect` (the old body verbatim, incl. the alert). Under `LeaveMatch`'s cover, or in the menu, it runs directly. |
| `ReturnToPartyLobby()` (RunResultPopup) | Covered like `LeaveMatch`, held until the async leave/join finishes (`_returningToPartyLobby == false`) **and** the scene is gone. Its offline branch calls `PerformLeaveMatch` directly - it is already covered. |

`IsInMatch()` (= the in-match window is the current one under `MainMenuTab`'s `WindowManager`) is the
guard that keeps `OnDisconnected` from raising a 2s screen for disconnects that happen in the menu
(party lobby, failed connect).

## Status (2026-09-21)

- **Wired:** Intro -> Menu (`IntroSequence`), and Game -> Menu via `LeaveMatch`, `OnDisconnected`,
  `ReturnToPartyLobby`. Compiles clean.
- **Untested in Play Mode.** The prefab was verified by loading it (wired, inactive, no camera/EventSystem
  inside) and the code compiles, but no transition has been run end to end - in particular that the
  gameplay scene reports `isLoaded == false` only once its unload really is done, and how the menu
  looks the moment the screen lifts.
- The prefab's `CanvasScaler` was Constant Pixel Size in `LoadingCanvas.unity`; it is now Scale With
  Screen Size 1920x1080 (match width) like the other canvases - identical at 1080p.
- `progressFill` / `progressLabel` are optional and unassigned; the prefab only has the pulsing logo and
  "LOADING..." text.
