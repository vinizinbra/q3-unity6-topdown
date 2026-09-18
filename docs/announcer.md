# Announcer (shared slide-in title banner)

`AnnouncerManager` is one shared "big banner" text announcement - fade + slide in, hold, fade +
slide out - for rare, dramatic, whole-team moments. Any HUD widget calls
`AnnouncerManager.Instance?.Announce("...")` instead of owning its own copy of the fade+slide tween
code.

## Why this exists

Three HUD widgets each reimplemented the exact same fade+slide banner, with identical authored
tuning values (0.2s/OutQuad fade-in, 0.5s/InQuad fade-out, 600px slide offset, 0.35s/OutCubic
slide-in, 0.3s/InCubic slide-out, 2.5s hold) copy-pasted three times:

- `BreathingWidget`'s own "AREA SECURED" sequence (the original).
- `SurvivalStartedWidget`'s "SURVIVAL MODE STARTED" (extracted from the above, but re-duplicated
  rather than shared).
- `AnnouncementBannerWidget`, used only by `TeamChallengeWidget` for "CHALLENGE STARTED/COMPLETE/
  FAILED" (a genuine extraction, but still its own separate instance/copy).

There was also an existing generic base, `AppearSlide`/`AppearSlideHorizontally`
(`UI/Common/AppearSlide.cs`), written to generalize exactly this shape but wired up nowhere in any
scene or prefab. `AnnouncerManager` is the one real consumer of it now.

## Architecture

- **`AppearSlide`** (`UI/Common/AppearSlide.cs`, unchanged behavior) owns the actual fade/slide/
  hold/auto-hide animation - `Show()`/`Hide()`, rest-position capture, unscaled time. It gained one
  addition for this system: a `Hidden` event, raised once `Hide()`'s slide/fade has fully finished
  and the GameObject has just been deactivated.
- **`AnnouncerManager`** (`UI/Common/AnnouncerManager.cs`) pairs one `AppearSlideHorizontally` +
  one `TMP_Text` and exposes:
  - `AnnouncerManager.Instance` - a registry-based static resolver, same pattern as `ToastManager`
    (a list of live managers, most-recently-registered wins), not a naive singleton field. Needed
    for the same reason: the gameplay HUD scene can reload independently of whatever else is
    loaded, and a plain static field would end up pointing at a manager destroyed with its old
    scene.
  - `Announce(string message, Action onComplete = null)` - swaps the label text and plays the
    banner (or queues it - see below). `onComplete` is GUARANTEED to eventually fire exactly once -
    immediately if `slide` isn't wired, or once this announcement's own turn has fully played out.
  - `IsPlaying` - true while a banner is currently showing OR one is queued behind it, for a caller
    that needs to keep its own UI up for the tail of an announcement.
  - `displayDuration` (own field, default 2.5s) - **`AnnouncerManager` owns and ticks its own
    hold-then-hide timer, calling `slide.Hide()` itself**, rather than relying on
    `AppearSlide.autoHideAfter`. Found as a real bug: `autoHideAfter` defaults to `0` ("never
    auto-hide, call `Hide()` yourself") and is easy to leave unauthored on a fresh scene instance -
    every `Announce()` would then show once and sit on screen forever, and anything waiting on the
    `Hidden` event to proceed would silently freeze. Managing the timer in code means `Announce()`
    works correctly regardless of that Inspector field's value.
  - `Start()` also force-hides `slide`'s GameObject if the scene happened to author it active -
    `Announce()` is the only thing that should ever show the banner, and this makes that true
    regardless of the slide GameObject's own initial active state in the scene (deferred to `Start`,
    not `Awake`, so `AppearSlide`'s own rest-position/alpha baseline capture - which runs in ITS
    `Awake` - already happened off the authored state before this deactivates it).

A real FIFO queue, not "restart in place": a second `Announce()` call while one is already playing
is queued and plays after the current one fully finishes, in order, rather than clobbering it. This
matters beyond ordering - every caller's `onComplete` is guaranteed to eventually fire (once its own
turn plays out), so a caller latching a bool on `Announce()` and clearing it in `onComplete` can
never get stuck permanently `true` from a competing `Announce()` overwriting its pending callback -
that was a real bug under the earlier restart-in-place behavior.

## `AnnouncerManager` owns the GameState-driven triggers directly

As of a later simplification pass (confirmed with the user), `AnnouncerManager` itself subscribes
to the real `EventGameStateChanged` Quantum event and decides when "AREA SECURED"/"SURVIVAL MODE
STARTED" fire - `BreathingWidget`/`SurvivalWidget` no longer call `Announce` for these at all, and
have zero announcer awareness:

```csharp
private void OnGameStateChanged(EventGameStateChanged e)
{
    if (e.NewState == GameState.Breathing && e.PreviousState == GameState.Survival)
    {
        Announce("AREA SECURED");
        return;
    }

    if (e.NewState == GameState.Survival
        && (e.PreviousState == GameState.Lobby || e.PreviousState == GameState.Breathing))
    {
        Announce("SURVIVAL MODE STARTED");
    }
}
```

This is deliberately checked against the event's own `PreviousState`/`NewState` fields directly,
NOT reconstructed from a locally-tracked "last state I saw" inside a widget. That distinction is
what fixed a real, repeated bug: a Team/Traversal Challenge overlay ending into an already-secured
Breathing (or an already-progressing Survival) has `PreviousState == TeamChallenge`/
`TraversalChallenge`, not `Survival`/`Lobby`/`Breathing` - so the rules above naturally skip it,
for free, with no widget-side edge-detection, latch, or "was this already secured while masked"
bookkeeping needed anywhere. Earlier attempts at this exact fix lived inside `BreathingWidget`/
`SurvivalWidget` themselves (tracking `_announcedThisWindow`/`_previousCombatPhaseState`) and each
needed a second, subtler fix once a masked-then-revealed edge case was found in testing -
centralizing the decision here, off the event Quantum already fires with the exact before/after
values, removes that whole class of bug instead of patching around it per widget.

This is also what lets `BreathingWidget`/`SurvivalWidget` be pure "listen to GameState, show/hide
myself" widgets - no delay, no waiting for the banner to finish before revealing their own content.
The countdown/timeline bar and the announcer banner are fully independent now: both can be on
screen at the same time, and that's fine.

## Current callers

- **`AnnouncerManager` itself** - "AREA SECURED" and "SURVIVAL MODE STARTED", off `GameStateChanged`
  directly (see above). Neither `BreathingWidget` nor `SurvivalWidget` reference `AnnouncerManager`
  at all anymore.
- **`TeamChallengeWidget`** - `Announce("CHALLENGE STARTED"/"CHALLENGE COMPLETE"/"CHALLENGE
  FAILED")` on its own matching Quantum events (`EventTeamChallengeStarted`/`Completed`/`Failed`) -
  these are NOT GameState transitions (they're tied to a specific challenge's own lifecycle, not a
  `Global.CurrentState` change), so this stays this widget's own responsibility rather than moving
  into `AnnouncerManager`'s `OnGameStateChanged`. No `onComplete`/latch - the widget's own
  visibility is a plain `IsActive(s) => s == GameState.TeamChallenge` match, same "no delay"
  simplification as the two GameState-driven widgets above.
- **`TraversalChallengeWidget`** - same shape, `Announce("TRAVERSAL CHALLENGE STARTED"/"COMPLETE"/
  "FAILED")` on its own Quantum events, no latch.

All 5 top-screen HUD widgets (`BossWidget`/`TeamChallengeWidget`/`TraversalChallengeWidget`/
`SurvivalWidget`/`BreathingWidget`) extend the shared `GameStateGatedWidget` base
(`Assets/_Project/Scripts/UI/InGame/Hud/GameStateGatedWidget.cs`), reacting to `GameStateChanged`
instead of polling - a separate pass from `AnnouncerManager` itself, but the two now compose
cleanly: each widget only answers "am I the state this represents," and `AnnouncerManager`
independently answers "does this specific transition deserve a banner."

## Explicitly out of scope

- **`BossWarningWidget`** ("BOSS APPROACHING in Ns!") - a persistent, countdown-tied fade overlay
  that stays shown for the whole warning window and updates its text every tick, not a one-shot
  show-hold-hide reveal. Left untouched.
- **`BossWindow`**'s boss-name reveal - a richer, staged multi-element reveal
  (`ShakeGrowImpactAnimation` per element, sequenced) alongside a full screen fade and camera
  cutaway, not a simple text banner. Left untouched.

Both were confirmed out of scope with the user when this system was introduced - folding either in
would mean bolting a "persistent"/"staged" mode onto `AnnouncerManager` for a single caller each,
rather than a real generalization.

## Current status

Code compiles; no `.qtn` change, so no codegen dependency.

### Editor authoring needed

Add one `AnnouncerManager` GameObject to the gameplay HUD canvas, with one child
`AppearSlideHorizontally` + `TMP_Text` - reuse the original "AREA SECURED" banner's authored
RectTransform/CanvasGroup/art as the visual (generalized to take dynamic text), and assign it to
`AnnouncerManager.slide`/`.text`. `BreathingWidget`/`SurvivalWidget`/`TeamChallengeWidget`/
`TraversalChallengeWidget` no longer hold any serialized reference to a banner themselves - only
`AnnouncerManager` needs one.

Not yet manually verified end-to-end in-Editor.
