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
