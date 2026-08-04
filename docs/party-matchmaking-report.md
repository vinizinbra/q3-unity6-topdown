# Party System Report: What `jelly-upgrade-q3` Has vs. What Rift Raiders Has

**Purpose:** compare the party/matchmaking system in the sibling project `jelly-upgrade-q3`
(a competitive kart racer) against Rift Raiders' current networking code, and recommend what's
worth porting. This is an analysis/proposal document, not a design spec for a shipped feature —
no code has been written yet.

---

## 1. Executive summary

`jelly-upgrade-q3` has a real **two-room party system**: players group up in a social "party
room," then the leader triggers a synchronized move into a separate match room (MMR-matched via
Photon's SQL lobby). Rift Raiders has **no party concept at all** — "Party" in the current UI is
just a label on the plain room-code create/join screen (`LobbyWindow`), and joining that room *is*
joining the match room directly. There's no roster/leader tracking via room properties, no event
that moves a group of players from one room to another, and no MMR matchmaking.

Rift Raiders is co-op PvE (up to N players clearing a run together), not competitive — so the MMR/
SQL-lobby half of the jelly system doesn't transfer as-is. **Decided (per user, 2026-08-03):**
quickplay-with-strangers doesn't matter for Rift Raiders — starting a party should never touch
matchmaking/a public room pool at all, it just moves the existing group into the run directly. That
removes the need to port jelly's "leave party room → SQL-lobby-filtered join into a different match
room" dance entirely. What transfers well, scoped down to just that, is the **roster/leader
tracking pattern** (`PartyManager`, custom room properties) layered on top of Rift Raiders' existing
single-room model. **Also decided: reconnect is an important, first-class requirement**, not a
nice-to-have — see §5/§6 for the concrete fix, which turns out to be small.

---

## 2. `jelly-upgrade-q3`: what exists today

Two Photon rooms in sequence:

1. **Party room** — a private, invisible custom room joined by a 5-digit code. `PartyManager`
   (a `PgSingleton`) tracks the roster (`playersInParty`/`partyPlayerInfos`) and leader, both stored
   as **custom room properties** (`partyMembers` CSV string, `masterClient` nickname) rather than
   native Photon Master Client — the real Master Client is force-reassigned to match this property
   whenever the designated leader (re)joins (`MatchMakingConfig.OnPlayerEnteredRoom`).
2. **Match room** — created/joined only when the leader calls `StartParty()`, which:
   - writes the final roster onto the party room's properties (housekeeping),
   - generates a **new** room code,
   - raises a cached, reliable Photon event (`SyncParty`, code 113) to everyone in the party room.

   Every client's `OnEvent(SyncParty)` handler then calls `LeaveAndJoinRoom(newCode)`: leaves the
   party room and joins/creates the match room together, carrying over MMR custom properties
   (`C0`/`C1` trophy-range bounds + `Map`) via a **SQL lobby filter**
   (`"C0 <= trophies AND C1 >= trophies"`). A party member who ends up alone (solo "party of one")
   falls back to `JoinRandomOrCreateRoomAsync` against the whole matchmaking pool instead of forcing
   a private room.

Supporting pieces:
- `MatchMakingConfig` is both the matchmaking *config* and the *runtime driver* — owns the single
  `RealtimeClient`, builds MMR windows from a remote-config-driven bracket list, and fully replaces
  the `Client` object on every disconnect (`ResetClient()`), broadcasting `OnClientReset` so other
  singletons (`PartyManager`) re-register as Photon callback targets.
- **Party restoration after a match**: an intentional client-side disconnect
  (`DisconnectCause.DisconnectByClientLogic`) calls `PartyManager.TryAutoJoinParty()`, which rejoins
  the last confirmed party room by name + region — all in-memory state, no PlayerPrefs.
- **Cross-session party restoration**: `PartyService` + CrazyGames platform invite links. A
  `roomId`/`region` deep-link parameter is parsed at SDK init, cached as a "pending invite," and
  `GameRouter.Route()` skips onboarding straight to the menu scene, where `PartyService` auto-joins
  that room.
- A vestigial, unused stock Quantum sample (`QuantumMenuUIParty`/`QuantumMenuPartyCodeGenerator`)
  also exists in that project but is dead code with zero references — worth ignoring, not porting.
- **The UI presentation layer for this whole flow is a `Popup`, not a full-screen menu window.**
  `PartyPopup` (Join or Create chooser) and `RoomPopup` (in-room roster/code/region/leader badge)
  are both popups shown over the existing menu, not separate screens you navigate the whole menu
  away to. This matches Rift Raiders' own direction (§6 item 4): the party UI living as one
  small, layered widget/popup over `MainMenuWindow`, not as a chain of full `UiWindow`s
  (`LobbyWindow`/`ConnectingWindow`/`RoomWindow`) the player is routed through.

## 3. Rift Raiders: what exists today

- **No separate party room.** `MainMenuWindow.Party()`/`OpenParty()` just open `LobbyWindow`, a
  plain 5-digit room-code create/join screen. `LobbyWindow.TryCreateRoom()`/`TryJoinRoom()` call
  `MatchMakingConfig.Instance.Quickplay(roomCode)` directly — the room you join by code **is** the
  match room. `MatchMakingConfig.CreateParty()` exists as a name but is an empty stub.
- **No roster/leader tracking via custom properties.** `RoomWindow` reads the live roster straight
  off `Client.CurrentRoom.Players` for display only; there's no `partyMembers`/`masterClient`
  bookkeeping, no forced Master Client reassignment.
- **`RoomWindow` already has a per-player `RoomWidget[] playerWidgets` roster array** — so the
  building block for showing teammates already exists as a `Widget`, it's just currently locked
  inside a separate full window you navigate to (`Show()`/`Hide()`, its own callback registration).
  **Decided (per user):** there should be no separate room window at all — instead, when in a room,
  `MainMenuWindow` itself should show a `RoomWidget` roster inline as part of the main menu screen,
  not a screen you navigate away to. This mostly means moving `RoomWindow`'s existing roster-driving
  logic (`UpdateUI()`, the `IInRoomCallbacks`/`IOnEventCallback` registration) onto/into
  `MainMenuWindow`, rather than writing it from scratch, and retiring `RoomWindow` itself.
- **No SyncParty-equivalent transition.** Clicking Start in `RoomWindow` doesn't move anyone to a
  different room — it just flips `IsVisible` and raises `WaitingForPlayers` (event 111) to advance
  UI state in place.
- **No MMR/SQL lobby.** Quickplay is a plain `JoinRandomOrCreateRoomAsync` against the default
  lobby — no trophies, no skill bracket, no `TypedLobby`.
- **Reconnect is broken/orphaned.** `matchmakingArguments.ReconnectInformation` is never assigned
  anywhere, so `CanReconnect` is always `false` and the Reconnect button never shows in the
  currently-wired UI. A legacy parallel path (`MatchMakingConfigOld`, `ReconnectWindow`, a
  PlayerPrefs-backed static `ReconnectInformation` class) still exists but appears to be
  mid-migration leftovers, superseded by `MatchMakingConfig`/`ConnectingWindow`.
- **No CrazyGames SDK** (or equivalent) in this project — so the invite-link deep-link mechanism
  in jelly has no direct platform hook to attach to here.
- Two duplicate `PhotonEventCode` enum definitions (`PhotonMain` and inside `MatchMakingConfig`)
  with identical byte values — a latent divergence risk, same smell present in jelly's own
  `PhotonMain`.
- "Party" HUD widgets (`PartyHudManager`/`PartyHudWidget`/etc.) are unrelated in-match UI (per-player
  health/cooldown strip) — not a grouping/matchmaking concept, just a naming coincidence.
- **No character-selection sync mechanism exists yet.** Grepping for character-select UI/state
  (`SelectedCharacter`, `CharacterSelect`, etc.) in `Assets/_Project/Scripts` turns up nothing — no
  window, no per-player custom property, no wiring into `RuntimePlayers`. `MatchMakingConfig.
  RuntimePlayers` is currently just a hand-populated `List<RuntimePlayer>` (looks like inspector-set
  test data), not built dynamically from live player choices.
- **No ready-state mechanism exists yet either.** `RoomWindow.StartClicked()` today just flips room
  visibility and raises `WaitingForPlayers` — there's no per-player "ready" flag gating it, so a
  leader can currently start before everyone's actually set up.
- **Decided (per user):** each player's pre-join choices are their **character** and a **Ready
  state** — no loadout, no map vote. Both are per-player state `PartyManager`/roster need to
  carry, and neither exists anywhere today — see §6 item 4.
- **Decided (per user): `MainMenuWindow`'s existing Play button is context-sensitive on party role.**
  Leader (or solo, no party) → button reads "Play" and actually starts the run once everyone's
  ready. Non-leader party member → the same button becomes "Ready" and just toggles that player's
  own `"ready"` custom property; it does not start anything. This needs `PartyManager.IsPartyLeader`
  (§6 item 3) wired into `MainMenuWindow`'s button label/behavior, not a second button.
- **No lightweight toast/feedback system exists.** This project has `AlertPopup`/`PopupManager`
  (`Assets/_Project/Scripts/UI/Popup/`), which are blocking, dismiss-required popups — there's
  nothing like jelly's `PgToastManager`/`PgToastWidget`
  (`jelly-upgrade-q3/Assets/_Project/Scripts/Runtime/MetaGame/Toast/`): a small pooled set of
  auto-fading, non-blocking message widgets (`PgToastManager.Instance.ShowMessage(string)`, ~5
  pre-instantiated `PgToastWidget`s reused round-robin via a `CanUse` flag, plus a static
  `ShowMessageAfterSceneLoad` for messages that need to survive a scene transition). Jelly uses this
  for exactly this kind of party feedback ("Party created", "X joined your party", join failures).
  **Decided (per user):** Rift Raiders needs the same kind of lightweight toast feedback for party
  actions — see §6 item 5.

## 4. Gap analysis

| Capability | jelly-upgrade-q3 | Rift Raiders | Relevant for Rift Raiders? |
|---|---|---|---|
| Social party room distinct from match room | Yes | No | **No** (decided) — no matchmaking pool to separate from, single room is fine |
| Roster/leader via custom room properties | Yes (`PartyManager`) | No | **Yes** — port this part, applied to the existing single room |
| Leader-triggered synchronized room transition (`SyncParty`) | Yes | No | **No** (decided) — nothing to transition into, party start goes straight into the run |
| Auto-rejoin party after a match ends | Yes (`TryAutoJoinParty`) | No | Worth considering once `PartyManager` exists, but secondary to reconnect itself |
| MMR / SQL lobby skill matchmaking | Yes | No | No — PvE co-op, not competitive; skip |
| Solo-party-of-one falls back to public matchmaking pool | Yes | No | **No** (decided) — no quickplay-with-strangers pool at all |
| Cross-session invite-link party restore (CrazyGames) | Yes | No | No platform hook available; would need a different mechanism (see §7) |
| Working reconnect-to-match | Partial (uses SDK `ReconnectInformation` correctly) | Broken (`ReconnectInformation` never set) | **Yes — top priority.** Root cause identified in §6: fix is a one-line initialization, not a redesign |
| Bot-difficulty calibration from player history | Yes (`AvgLap`) | N/A | No — racing-specific, not applicable |

## 5. Recommendation

Port only the **roster/leader tracking** half of jelly's pattern (`PartyManager`, custom room
properties for membership/leadership), not the two-room transition. Since matchmaking-with-
strangers is explicitly out of scope, there's no reason to leave one room and SQL-lobby-filter into
another — Rift Raiders' existing model (join-by-code room *is* the match room) stays as-is; a
`PartyManager` just adds a proper roster/leader layer on top of that single room so the main menu
(via an inline `RoomWidget` roster, not a separate `RoomWindow` — see §6 item 4) can show who's the
leader, gate "Start" to the leader, and support "kick"/"leave" cleanly, instead of reading
`Client.CurrentRoom.Players` ad hoc for display only.

Treat **reconnect as equal priority, not an afterthought** — it's a core requirement, and per §6 the
actual fix is small: the SDK (`MatchmakingExtensions.ConnectToRoomAsync`) already auto-populates
`MatchmakingArguments.ReconnectInformation` on a successful join, *but only if that field is
non-null before the call*. `MatchMakingConfig` currently never constructs one, so the SDK's own
auto-populate step is silently skipped every time. This is a small, well-contained fix, not a
redesign.

Concretely, in Rift Raiders' co-op run context:
- The party room/match room stay the same single room — no new room code, no leave-and-rejoin, no
  `SyncParty` event needed. `PartyManager` is purely a roster/leader bookkeeping layer on the room
  that already exists.
- Skip MMR entirely — co-op doesn't need skill brackets, and there's no quickplay-with-strangers
  pool to filter into.
- Skip the CrazyGames invite-link flow entirely — no target platform for it today (see open
  questions).

## 6. Concrete implementation plan

Suggested phases, mapped onto files that already exist in this project or mirror the jelly pattern.
Reconnect (item 1) is called out first since it's the priority, is the smallest change, and is
independent of the roster work.

1. **Fix reconnect** (`MatchMakingConfig.cs`) — confirmed root cause by reading the SDK source
   (`Assets/Photon/PhotonRealtime/Code/MatchmakingExtensions.cs`):
   `ConnectToRoomAsync(MatchmakingArguments arguments, ...)` already does
   `if (arguments.ReconnectInformation != null) arguments.ReconnectInformation.Set(client);` right
   after a successful join (line ~113-115), i.e. **the SDK auto-populates it for you** — Room name,
   region, UserId, and a timeout window get stamped automatically. The only reason `CanReconnect`
   is stuck at `false` today is that `matchmakingArguments.ReconnectInformation` is never
   constructed in the first place, so that `if` is always skipped. The fix is narrow:
   - In `Awake()` (or wherever `matchmakingArguments` is first built), set
     `matchmakingArguments.ReconnectInformation = new MatchmakingReconnectInformation();` once.
   - Leave `Connect()`/`Quickplay()` alone — every future successful join will now auto-refresh it.
   - Verify `ReconnectAsync()` → `Client.ReconnectToRoomAsync(matchmakingArguments)` actually reaches
     `RejoinRoomAsync`/`JoinRoomAsync` against `ReconnectInformation.Room` (SDK lines ~297-307) and
     that `MainMenuWindow.Reconnect()` sets `matchMakingType = RECONNECT` (it currently sets
     `QUICKPLAY`, which looks like a bug independent of the `ReconnectInformation` issue and should
     be fixed alongside it).
   - Decide what happens on reconnect after a run has *already ended*: jelly's equivalent case
     (`OnDisconnected` with `DisconnectByClientLogic`) routes back to the party rather than trying to
     rejoin a room that's gone — worth an explicit check here too once `PartyManager` exists (item 3).
2. **Decide what to do with `MatchMakingConfigOld`/`ReconnectWindow`** — do this before or alongside
   item 1, since both files currently hold a second, dead reconnect path
   (PlayerPrefs-backed static `ReconnectInformation` class, `IsRejoining` flag) that could confuse
   testing which fix actually worked. Recommend deleting the old path once the new one is confirmed
   working, rather than maintaining two.
3. **`PartyManager` (new, `Assets/_Project/Scripts/PartyManager.cs`)** — a roster/leader layer over
   the *existing* single room (no new room, no `SyncParty`, no leave/rejoin). Singleton matching
   whatever base class this project already uses for `MatchMakingConfig` (check before introducing
   `PgSingleton` fresh — that's a jelly-project type, may not exist here), implementing
   `IInRoomCallbacks`/`IOnEventCallback` against `MatchMakingConfig.Instance.Client`. Tracks the
   roster and a designated leader via custom room properties (`partyMembers`/`masterClient`, same
   CSV-property trick as jelly, or native Master Client if that's simpler here — worth a quick
   design call rather than assuming jelly's approach is necessary once there's no second room to
   coordinate leadership across). Exposes `IsPartyLeader`, fires an update event for UI.
4. **Fold `RoomWindow`'s roster into `MainMenuWindow` via `RoomWidget`, and make the Play button
   context-sensitive** — no separate room window (decided per user, see §3):
   - **Collapse the party flow into one 3-state widget, not three separate windows.** Today the
     party path is three separate `UiWindow`s the player navigates between:
     `LobbyWindow` (choose Join or Create Party, enter a room code) → `ConnectingWindow` (shows
     `Client.State` while joining, then routes to `RoomWindow` on `OnCreatedRoom()`/`OnJoinedRoom()`
     for `MatchMakingType.CUSTOM`) → `RoomWindow` (in-room roster). **Decided (per user):** this
     becomes a single embedded widget with 3 internal states instead — same idea as `RoomWidget`'s
     own inactive/active-slot pattern, one level up: **(a) before joining** — Join/Create Party
     choice (`LobbyWindow`'s content), **(b) joining** — connecting feedback (`ConnectingWindow`'s
     content, for the `CUSTOM` path), **(c) in room** — the `RoomWidget` roster + Play/Ready button
     (`RoomWindow`'s content). Each existing window's logic moves in largely as-is (`TryCreateRoom`/
     `TryJoinRoom`, the `IMatchmakingCallbacks` connecting-state handling, `RoomWindow`'s
     `IInRoomCallbacks` roster driving) — this is a state-consolidation, not a rewrite. Quickplay's
     own `ConnectingWindow` → `WaitingForPlayersWindow` path is unaffected since it isn't part of the
     party flow.
   - **`RoomWidget` today** (`Assets/_Project/Scripts/UI/Window/RoomWidget.cs`) is minimal — one
     `TMP_Text name`, and `Setup(string playerName)` toggles the *whole widget's* active state
     (`gameObject.SetActive(!string.IsNullOrEmpty(playerName))`): a slot is either inactive/free (no
     player assigned) or active and showing a name. **Decided (per user):** it also needs to show
     ready state per occupied slot — add a `readyObject` (child `GameObject`, e.g. a checkmark/badge)
     that `Setup` shows/hides independently of the name, so `Setup` becomes something like
     `Setup(string playerName, bool isReady)`: widget inactive when the slot is empty, active with
     `readyObject` toggled on/off when occupied, driven by that player's `"ready"` custom property.
   - Move `RoomWindow`'s existing `RoomWidget[] playerWidgets` roster-driving logic (`UpdateUI()`,
     the `IInRoomCallbacks`/`IOnEventCallback` registration currently in `RoomWindow.Show()`/
     `Hide()`) onto `MainMenuWindow` (or a small always-present sub-component of it), driven off
     `PartyManager`'s roster instead of raw `Client.CurrentRoom.Players`. Retire `RoomWindow` once
     this is live. `LobbyWindow.TryCreateRoom()`/`TryJoinRoom()` stay effectively as-is (still
     `MatchMakingConfig.Quickplay(roomCode)` into the one room) but should initialize
     `PartyManager`'s roster tracking once joined, and `MainMenuWindow` should simply show/hide the
     `RoomWidget` roster based on `PartyManager.HasParty`/`Client.InRoom` rather than navigating to
     a different screen.
   - **`MainMenuWindow`'s Play button becomes context-sensitive on `PartyManager.IsPartyLeader`**:
     leader (or no party) → label/behavior stays "Play", starts the run once every player's `"ready"`
     property is true. Non-leader party member → same button reads "Ready" and just toggles that
     player's own `"ready"` custom property — it must not be able to start anything.
   - **Two pieces of per-player state the roster needs to carry** (see §3), both simplest as Photon
     **custom player properties** on the single existing room (mirrors jelly's
     `PublishLocalPlayerProperties`/`bcProfileId` pattern — no new room/event needed):
     - `"character"` — published the moment a player picks in the UI; roster reads it back for
       every player and reacts to `OnPlayerPropertiesUpdate` so picks update live for everyone.
       At `StartRunner()`, build each `RuntimePlayer` from this property instead of the current
       hand-populated `RuntimePlayers` list — this is the missing link between "player picks a
       character" and "the Quantum sim actually spawns that character."
     - `"ready"` (bool) — toggled via the Play/Ready button above once a player's picked a
       character; drives each occupied `RoomWidget`'s `readyObject` (above). The leader's Play button should
       require every player's `"ready"` property being true before actually raising
       `WaitingForPlayers`/`StartGame` — otherwise disabled/no-op, so the leader can't start before
       everyone's set. Worth deciding whether picking a character auto-sets ready or whether it's a
       separate explicit toggle.
5. **New toast/feedback manager** (mirrors jelly's `PgToastManager`/`PgToastWidget`, per §3) — a
   small pooled set of `Widget`-suffixed toast components (e.g. `ToastManager`/`ToastWidget`,
   following this project's UI naming convention) living alongside the existing
   `AlertPopup`/`PopupManager` in `Assets/_Project/Scripts/UI/Popup/`, but non-blocking/auto-fading
   rather than dismiss-required. A singleton `Instance.ShowMessage(string)` API, a handful of
   pre-instantiated, reusable widget instances (round-robin via a "can I be reused right now" flag,
   same as jelly's `CanUse`), fires feedback for party actions: party created, player joined/left,
   join failed, marked ready, etc. Needed before item 4's Play/Ready flow ships, since that's exactly
   the kind of transient feedback it's for.
6. **Two duplicate `PhotonEventCode` enums** (`PhotonMain` and inside `MatchMakingConfig`) — worth
   collapsing to one while touching these files anyway, independent of the party/reconnect work but
   low-risk to fix in the same pass.

## 7. Open questions for the user

- **Cross-session invite links**: jelly solves "drop a friend straight into my party" via CrazyGames
  deep links. Rift Raiders has no such SDK — is there a target platform (Steam, itch, a custom
  backend) that could carry an equivalent `roomId`/region param, or is in-session room-code sharing
  (already possible today) good enough for now?
- **Legacy cleanup scope**: should `MatchMakingConfigOld`/`ReconnectWindow`/the duplicate
  `PhotonEventCode` enum be cleaned up as part of this work, or tracked separately?
