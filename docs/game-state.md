# Game State

A structured top-level match-flow state machine (`Global.CurrentState`, a `GameState` enum) -
replaces what used to be independent, ad hoc `Global` booleans for each phase (`LobbyExited`) with
a single source of truth every phase-gated system checks the same way. `Global.LevelUpScreenOpen`
(the level-up/Chest upgrade-choice screen's own flag, `Experience.qtn`) still exists unchanged
alongside it - see "Why `LevelUpScreenOpen` wasn't replaced" below.

## Design choices (explicitly confirmed with the user)

- **`GameState : Byte { Lobby, Survival, Upgrade, Event, Boss }`** - the user's own spec. `Lobby`
  is deliberately value 0 so a fresh match starts there with no explicit seeding (`Global` starts
  zeroed). Only `Lobby`/`Survival`/`Upgrade` are actually wired today - `Event`/`Boss` are
  scaffolded (declared, documented, nothing transitions into or out of them yet), matching this
  codebase's own convention for reserved-for-later fields (`BalanceConfig`'s
  `ExpectedPlayerDps`/`EliteFrequency`, `RuntimePlayer.HasUnlockedRift`/`CanFindStones`/`HasEvent`).
- **"Pause time" means different things per state, deliberately not centralized in one
  mechanism.** `Lobby` must NOT pause `GameplaySystemGroup` (players need to move to ever leave
  the lobby) - only `CombatDirectorSystem` (the Survival Director / `Global.SurvivalTime`) stays
  idle. `Upgrade` pauses the WHOLE `GameplaySystemGroup` (the pre-existing mechanism, unchanged) -
  since `CombatDirectorSystem` itself lives inside that group, the Director freezes for free
  without needing its own explicit `GameState` check for this case (the check is still there
  anyway, see below). `Event`/`Boss` aren't wired yet, so their own pause shape isn't decided -
  flagged as an open question in "Editor/design work needed" below.
- **`GameStateUtility.SetState` is deliberately thin** (set the value, fire an event, no-op if
  unchanged) - it does NOT itself decide whether a transition pauses anything. Each transition's
  own pause behavior is owned by whichever system/utility drives it (`LobbyBoundarySystem` for
  `Lobby`->`Survival`, `LevelUpUtility` for `Survival`/`Lobby`<->`Upgrade`), since that behavior
  genuinely differs per state and baking it into one central function would need an
  ever-growing per-state switch there instead of at the actual call site that already knows the
  context.
- **`Upgrade` always restores whichever state it interrupted, never hardcodes `Survival`.** A
  talent-granted `Chest` (`docs/talents.md`) can be opened while still in `Lobby` (before a
  player has walked out of the boundary) - `Global.PreUpgradeState` is captured the instant
  `Upgrade` starts and restored verbatim in `LevelUpUtility.Resolve`, so that case correctly
  returns to `Lobby`, not `Survival`.

## `GameState.qtn`

```
enum GameState : Byte
{
    Lobby, Survival, Upgrade, Event, Boss
}

global
{
    GameState CurrentState;
    GameState PreUpgradeState;
}
```

## `GameStateChanged` event (`Events.qtn`)

```
event GameStateChanged
{
    GameState PreviousState;
    GameState NewState;
}
```

Fired once by `GameStateUtility.SetState` per actual change (a same-state call is a no-op, no
event) - match-wide rather than entity-scoped, unlike almost every other event in `Events.qtn`.
Nothing subscribes to it on the View side yet - simulation-only pass, per the user's own scoping
("later we can see about the windows").

## `GameStateUtility.cs`

```csharp
public static void SetState(Frame f, GameState newState)
{
    GameState previous = f.Global->CurrentState;
    if (previous == newState) return;
    f.Global->CurrentState = newState;
    f.Events.GameStateChanged(previous, newState);
}
```

## Wired transitions

- **`Lobby` -> `Survival`**: `LobbyBoundarySystem` (`docs/talents.md`) - once any one connected,
  spawned player has walked outside the `LobbyStart` chunk's own footprint (every connected player
  must have spawned first; the exit itself is first-one-out, not everyone-out). Its own early-return
  guard changed from `LobbyExited == true` to `CurrentState != GameState.Lobby` - a stricter,
  more correct guard than the old boolean: if `Upgrade` is currently interrupting `Lobby` (the
  Chest-opened-before-leaving case above), this system now correctly skips its own check instead
  of potentially racing `LevelUpUtility`'s own state management.
- **`CombatDirectorSystem`**'s gate changed from `LobbyExited == false` to
  `CurrentState != GameState.Survival` - the Director now only ever runs during `Survival`,
  explicitly (not just "whatever isn't Lobby"), so `Upgrade`/`Event`/`Boss` are all covered by
  the same one check without needing their own bespoke gate later.
- **`Survival`/`Lobby` <-> `Upgrade`**: `LevelUpUtility.OpenUpgradeScreen`/`Resolve` (unchanged
  `LevelUpScreenOpen`/`SystemDisable<GameplaySystemGroup>` mechanism, just now also calling
  `GameStateUtility.SetState`). `OpenUpgradeScreen` captures `Global.PreUpgradeState =
  Global.CurrentState` before switching to `Upgrade`; `Resolve` calls
  `GameStateUtility.SetState(f, f.Global->PreUpgradeState)` instead of assuming `Survival`.

## Why `LevelUpScreenOpen` wasn't replaced

`Global.LevelUpScreenOpen`/`LevelUpTimeRemaining` (`Experience.qtn`) still exist and still do all
the actual work (pausing `GameplaySystemGroup`, driving `LevelUpSystem`'s own countdown/resolve
loop) - `GameState.Upgrade` is a parallel, higher-level label for the same window, not a
replacement for the mechanism. Collapsing them into one would mean `LevelUpSystem` reading
`CurrentState == GameState.Upgrade` instead of the dedicated boolean, which is a real option but
out of scope for this pass - not requested, and `LevelUpScreenOpen` is load-bearing elsewhere
(`ChestSystem`'s own re-entrancy guard, `docs/chests.md`) in ways worth touching deliberately, not
as a side effect of this refactor.

## Editor/design work needed

- `Event`/`Boss` states are pure vocabulary right now - nothing transitions into or out of
  either. `Event` mirrors the already-scaffolded `RuntimePlayer.HasEvent`/
  `Global.SharedHasEvent` (`docs/talents.md`), but no actual "Event" system/encounter exists yet
  to trigger it. `Boss` would need a hook into the existing `BossSystem`'s own encounter
  entry/exit (not investigated as part of this pass).
- Undecided: does `Event` pause the whole `GameplaySystemGroup` (like `Upgrade`) or just the
  Director (like `Lobby`/intended `Boss`)? The user described both `Event` and `Boss` as "pause
  time" without specifying which shape - resolve this when either is actually built.
