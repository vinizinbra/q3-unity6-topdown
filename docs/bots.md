# Local-testing Bots

A bot is a **real Quantum player slot whose `Input` is produced by the simulation instead of a
device**, so one person can start a full co-op party and watch a hero's kit fire in a real match.
It exists to make heroes easy to test and easy to play with locally - it is deliberately not an AI
opponent system, and nothing here is intended to ship as a gameplay feature.

Target scenario from the brief: **1 human + 2 bots**, started from `QuantumRunnerLocalDebug` in
`QuantumGameScene`.

## The two halves

**Simulation** - a bot is just a player who happens to have a `BotBrain` component:

| File | Role |
| --- | --- |
| `Simulation/QTN/Bot/BotBrain.qtn` | `component BotBrain { Input Data; FP HeroSkillTimer; FP DashSkillTimer; FP LeashTimer; }` |
| `Simulation/Systems/Bot/BotInputSystem.cs` | Writes `BotBrain.Data` every tick - the whole AI |
| `Simulation/Systems/Player/PlayerInputUtility.cs` | `Resolve(f, entity, playerLink)` -> `Input*`, bot or human |
| `RuntimePlayer.User.cs` | `public bool IsBot` - the authored checkbox |
| `RuntimeConfig.User.cs` | `BotSettings Bots` - all tuning, under the existing `[Header("Debug")]` |
| `PlayerSpawnUtility.Spawn` | The **only** place `IsBot` is read: turns it into a `BotBrain` |

`RuntimePlayer.IsBot` is read exactly once, at spawn, and becomes a component. Everything
downstream keys off the component (`f.Has<BotBrain>`), so no per-tick path ever fetches a
`RuntimePlayer` to ask what a bot is.

**View** - a bot must never be mistaken for a local player:

| File | Change |
| --- | --- |
| `View/Util/QuantumHelper.cs` | `GetLocalSlotIndex` returns -1 for a bot, and bots don't **consume** a slot |
| `View/Entities/Player/CharView.cs` | Sets its own pre-existing `isBot` flag |
| `Photon/Quantum/Runtime/QuantumDebugInput.cs` | Polls empty input for a bot slot |
| `_Project/Scripts/MatchMakingConfig.cs` | Doesn't stamp the local character choice onto a bot's `PlayerAvatar` |

## Why the input goes through the simulation

`PlayerInputUtility.Resolve` is the single choke point. A real player's `Input` comes from the
deterministic input stream (`f.GetPlayerInput`); a bot's comes from its own `BotBrain.Data`,
written earlier the same tick. Both are the same `Input` struct, so **every consumer is a one-line
swap and none of them knows a bot exists**:

- `PlayerMovementProcessor` (movement, run, auto-hop/auto-mantle)
- `SkillSystem` (Dash / Hero Skill / the POI interact redirect)
- `AutoJumpSystem` (manual jump)
- `ReviveChannelSystem` (the hold)
- `WeaponSystem` (only under `DebugManualFireInput`)

This is the same "fake a player's Input on an entity" shape `InputSource.qtn` already established
for Lux's sentry gun. It is deliberately a **second** component rather than reusing `InputSource`,
because a bot really does have a `PlayerLink`, and `WeaponSystem.HasFireDriver` reads "has
`InputSource`" as "is a non-player shooter".

The alternative - synthesizing bot input on the View side inside `QuantumDebugInput.PollInput` -
would have needed zero simulation changes, but it puts AI in the View, reading a predicted frame,
and it only works for whichever client owns that slot. Keeping it in the simulation means it is
deterministic, replay-correct, and would survive a bot being added to an online match.

## What the bot actually does

`BotInputSystem` runs inside `GameplaySystemGroup`, immediately before `KCCSystem` - so the
decision it makes is the one this same tick's movement resolves, and a bot freezes along with
everyone else when an upgrade screen pauses the group.

- **Follow the first player.** Lowest-`PlayerRef` non-bot wins, so a bot trails the same person all
  run rather than swapping to whoever is closest. Falls back to another bot if there is no human.
- **Stop-and-go with hysteresis.** Parks at `FollowDistance` and won't set off again until the
  target is a `FollowSlack` further out. Holds `Run` past `RunDistance`.
- **Formation slot, not the target's exact spot.** Each bot has its own randomized (angle, distance)
  offset - resolved every tick against the target's *current* facing (`Aim.Angle`), so the slot
  swings around as the target turns, and re-rolled to a new angle/distance only on its own
  `[FormationRerollIntervalMin, FormationRerollIntervalMax]` timer, not every tick. This is what
  keeps two bots following the same person from stacking on top of them or each other (replaces the
  old fixed per-slot spread). A Downed/KO target is the one exception - the bot walks to their
  *exact* position instead, since it needs to be inside the Revive Interactable's radius.
- **Wall deflection, no pathfinding.** Reuses `EnemyMovementUtility.SteerAroundWalls` - the same
  deflection an `AvoidWalls` enemy gets.
- **Ledge avoidance.** See below - the bot will not walk into a pit, and walks along the lip of one
  to get where it is going. Wall deflection and ledge avoidance together are the entire navigation
  story.
- **Leash.** Stranded for `LeashTimeout` seconds -> `KCC.Teleport` back next to the target, velocity
  zeroed. "Stranded" is either further than `LeashDistance` away **or** blocked by a ledge with no
  safe route at all. This is the recovery for the missing pathfinding, not a movement mode.
- **Skills on a timer.** Dash and Hero Skill each pulse on their own countdown, re-rolled inside a
  `[min, max]` band after every press so two bots never lock into unison. Hero Skill additionally
  requires an enemy within `HeroSkillEnemyRange` - watching a bot fire its ultimate into an empty
  room is exactly the thing that wastes a test run.
- **Never fires a weapon.** It doesn't have to: firing is auto-attack off `Aim.Target`
  (`WeaponSystem.HasFireDriver`), and `AimSystem` treats a bot like any other player. `Input.Fire`
  is never written.
- **Revives a downed teammate**, and closes to `DownedTargetFollowDistance` to do it. This is the
  one interaction a bot takes deliberately, because otherwise solo-testing means every death waits
  out `Global.BreathingAreaSecured` (see `docs/revive.md`).
- **Touches nothing else interactive while it has a leader.** The Hero Skill button is also the
  interact button, so the bot skips its cast entirely while standing on any other
  `Available`/`NotNeeded` POI - no bots quietly drinking the Healing Shrine or opening the Store.
  This does not apply to a **solo** bot's own Store visit - see below.

## Solo behavior (nobody to follow)

`TryResolveFollowTarget` fails only when this bot is genuinely the last player standing - no
human, no other bot. A bot with a leader is completely unaffected by this section; only the one
leaderless bot gets it, instead of standing frozen for the rest of the run.

- **Breathing Break, area secured: walk to the Store and buy one weapon.** Only once
  `Global.BreathingAreaSecured` is true - the same flag `PoiAvailabilityUtility.IsAvailable` itself
  gates the Store on for anyone, since the whole point of that flag is "the encounter is actually
  cleared." Bypasses the normal `ContextInteraction`/Command dance entirely - a bot has no screen
  to open a `ChooseWindow` on - and calls `StoreUtility.TryBeginInteraction`/`BuyWeapon`/`Close`
  directly instead, the same "call the utility, skip the Command" idiom
  `LevelUpSystem.AutoPickForBots` already uses via `LevelUpUtility.AutoConfirm`. Buys the first
  offer it hasn't already bought this Break, regardless of Coin balance -
  `StoreUtility.BuyWeapon`'s own `CoinUtility.TrySpend` guard just no-ops if it can't afford it.
  Once per Breathing Break (`BotBrain.StoreAttemptedAtBreathingIndex`, compared against
  `Global.BreathingIndex` the same way `StorePurchaseEntry` already is), and only marked attempted
  once the interaction actually opens.
- **Breathing Break, area NOT yet secured: fight instead.** `Global.BreathingAreaSecured` starting
  false means enemies from the encounter are still alive - a bot that beelined for the Store anyway
  would just stand there uselessly retrying `TryBeginInteraction` every tick while its teammates
  fight. Runs the same Elite-priority/combat-target hunt as normal Survival (below), just with
  chunk exploration turned off (`allowExploration: false` - map discovery isn't the point of a
  Breathing Break) - kill what's left, then the Store becomes reachable on its own.
- **Otherwise (normal Survival): hunt enemies/XP while also discovering the map.** Re-picks a goal
  (`BotBrain.SoloTarget`) every `SoloRepickInterval` seconds, or immediately if the current one
  stops being valid (its `Chunk` got `Discovered`, its enemy died - or is merely lingering through
  its death animation, `EnemyActionPhase.Dead` - its `CurrencyOrb` got collected). An **Elite**
  enemy (`EnemyDataAsset.Tier == EnemyTier.Elite`) always wins outright over everything else,
  map-wide rather than range-limited - a Director Elite spawn is a deliberate, rare encounter the
  run wants dealt with, not just something to bump into. Short of that, while any `Chunk` is still
  undiscovered, a coin flip decides whether to prioritize walking into the nearest undiscovered one
  or chasing the nearest enemy/XP orb (falling back to the other if its own pick comes up empty);
  once every `Chunk.Discovered` is true, it always hunts. Reuses the exact same wall-deflection/
  ledge-avoidance steering as following a player - see `BotInputSystem.SteerToward` - just
  retargeted at a `Chunk`'s center or an enemy's/orb's `Transform3D.Position` instead of a player.
  If every direction runs off a ledge, the goal is dropped (not leashed - there's no "home"
  position to return to while solo) and a new one is picked next tick.
- **Every candidate is filtered to what's actually reachable BY LAND.** This project has no
  distinct water collider (every body of water is mechanically identical to a bottomless pit -
  see `PlayerMovementProcessor`'s own comment), so straight-line "nearest" alone would happily send
  a solo bot marching into a lake separating it from an undiscovered chunk, an Elite, or any other
  target on the far side. `BotInputSystem.CollectReachableChunks` walks `Chunk.ConnectedChunks` (the
  same land-adjacency graph `ChunkConnectivityUtility`/Director spawning already trusts) from the
  bot's own chunk, and every search checks candidates against that set. Falls back to unfiltered if
  the bot's own position can't be resolved to a containing chunk at all, rather than refusing to
  pick anything.

## Void avoidance

Following a player in a straight line means that line eventually points across a chasm - and the
player **auto-hop makes that actively dangerous rather than merely clumsy**. `PlayerMovementProcessor`
reacts to "no ground ahead" by *jumping*, so a bot that walks at a pit doesn't stop at the edge, it
gets launched into it. Falling forever was the observed symptom.

So the bot rejects an unsafe direction **before** auto-hop's own probe can fire, which is the whole
reason `LedgeProbeDistance` (1.5) reaches further than `MovementDataAsset.EdgeProbeDistance` (0.75).

`TryFindSafeDirection` tries the direct route, then mirrored deflections at ±45° and ±90°, and takes
the first one that is safe. The ±90° candidates are what let it walk **along** the lip of a chasm
toward the target instead of stopping dead at it. This runs *after* `SteerAroundWalls`, on purpose -
a direction slid along a wall can just as easily end up pointing off a ledge as the original did.

`IsDirectionSafe` accepts a direction on either of two grounds, cheapest first:

1. `HasGroundAhead` within `LedgeMaxDropDistance` (4) - flat floor, a step down, or a real ledge the
   bot can walk off and live.
2. `TryFindGapLanding` out to `LedgeMaxCrossableGap` (1.5) - nothing at the probe point, but solid
   ground reappears close enough that the auto-hop clears it. This case exists mostly for **chunk
   seams**, which are sub-unit and far more common than actual chasms; without it a bot stops dead
   at every seam it meets. The landing's *height* is re-tested against the same drop limit, because
   `TryFindGapLanding` samples via `TryFindGroundHeight`, which looks 20 units down - otherwise a
   deep-but-floored pit would come back as a crossable gap.

Anything else is a pit, and if **every** candidate is a pit the bot stands still and reports itself
blocked, which promotes it to "stranded" for the leash. That matters because the target can be only
a few units away on the far side of a chasm, where a distance-only leash would never fire and the
bot would stand there for the rest of the run.

The probes are skipped entirely while airborne - they measure the ground under a *walking* path,
which says nothing useful mid-jump, and there is no steering decision left to protect. A bot that
gets knocked into a pit anyway is handled the same way a player is: `PlayerFallSystem` catches it at
`LevelConfig.FallDeathHeight`, deals fall damage and respawns it.

### Two path tiers - a chunk-level macro route, and a local grid detour per leg

`TryFindSafeDirection`'s 5 candidates are a single-step heuristic that only looks ~2-3 units ahead
of THIS tick's position. That is exactly wide enough to match `MovementDataAsset`'s own
`WaterMaxCrossableGap`-style auto-hop reach for a legitimate narrow gap - which is also exactly wide
enough to misread a real body of water as "just hop it" one step at a time, since nothing about the
check looks further than one hop past the current edge. A bot that only re-checks reactively AFTER
`SteerToward` reports blocked can therefore walk (or auto-hop) straight into open water without
`SteerToward` ever once returning blocked - the false "safe" reading never lets a reactive-only
fallback trigger at all. This is why solo movement doesn't wait to be told it's stuck, and why it
routes chunk-to-chunk instead of trusting one long straight line:

- **`RoutePath` (macro, solo-only, PROACTIVE): `TryFindChunkRoutePath`** walks the level's own
  authored `Chunk.ConnectedChunks` adjacency graph (the same graph
  `CollectReachableChunks`/`ChunkConnectivityUtility`/Director spawning already trust) from the
  bot's own chunk to the target's chunk, turning the resulting chunk SEQUENCE into one waypoint per
  intermediate chunk's center plus the real target position as the final waypoint. `EnsureRoutePath`
  builds this BEFORE the bot ever takes a step toward a stationary-ish goal (a Chunk/enemy/orb/
  Store) - a route through chunks the level itself guarantees are land-connected is far more
  trustworthy than raycasting one flat grid across the whole straight-line distance with no notion
  of chunk boundaries at all. Declines (same chunk, or a position that can't be resolved to a chunk)
  rather than building a meaningless single-chunk "route".
- **`DetourPath` (local, shared, REACTIVE): `TryFindGridPath`** is a BFS over a grid of raycast-down
  samples (`GridStep` apart, spanning the straight line between bot and target plus
  `GridSearchPadding`, step size scaling up via Chebyshev distance / `MaxGridPathWaypoints` to stay
  inside the array's capacity regardless of distance) - tried only once `SteerToward`'s direct route
  for the CURRENT leg is found blocked. Each grid edge only checks floor presence and a walkable
  height step between neighboring cells (`GridStepMaxHeightDelta`, same spirit as
  `LedgeMaxDropDistance`) - this is about routing around voids/water, not indoor walls, which
  `SteerAroundWalls` still handles per leg once a waypoint is resolved. It always leads back to
  whichever leg's own destination was blocked (a `RoutePath` waypoint, the real target, or a follow
  target directly) - never the far end of the whole journey - so one pond inside one chunk along a
  macro route only reroutes that single hop; the rest of `RoutePath` is untouched. This is also the
  whole mechanism for a follow target (no macro route - a live player moves every tick, so one
  computed against last tick's position would already be stale) and for solo's same-chunk case.

**`TryMoveToward`** is the shared entry point every bot movement path goes through - checks
`DetourPath` first (an in-progress local detour), then `RoutePath` (the current macro leg, if any),
else the real target directly - and only reports true "stuck" once the current leg's direct route
AND a fresh `TryFindGridPath` for it have both failed. That's what UpdateFollow promotes to the
leash, and what solo wander drops the whole goal over (picking a new one next tick).

Entirely bot-only - nothing here is shared with or reused by any real gameplay/enemy system.

## Not making the human wait

A bot has nobody at the keyboard, so it removes itself from every "waiting for all players" gate
rather than making the human sit through a timeout. Both are opt-out via `RuntimeConfig.Bots`:

- **Level-up** (`LevelUpSystem.AutoPickForBots`): a bot random-picks its own option the tick the
  screen opens, via `LevelUpUtility.AutoConfirm` - the exact same random draw `Resolve`'s own
  30-second timeout fallback would have made. This changes *when* a bot picks, never *how*. The
  human's own screen is untouched, and the screen closes as soon as they choose.
- **Breathing skip vote** (`RunPhaseUtility.ProcessSkipVotes`): a bot never sends a
  `SkipBreathingCommand`, so without an auto-vote the unanimity check could never pass in a bot
  party and the human's Skip button would silently do nothing.

## Local-slot arithmetic

This is the subtle half. On a `QuantumRunnerLocalDebug` session the bots are literally *this
client's own local players* - `game.AddPlayer(i, LocalPlayers[i])`. Without an exclusion, every
"is this mine" View path (`FollowCamera` targets, `MyLocalPlayer` slots, every
`BindToSlot(0, ...)` HUD widget, `GameplayUiController.choiceWindows[]`) would happily adopt one.

`QuantumHelper.GetLocalSlotIndex` is the single place that is fixed, and it does two things:

1. A bot resolves to **-1** - never local, so `CharView.Initialize` never registers it with
   `MyLocalPlayer`, it never becomes a camera target, and nothing binds HUD to it.
2. A bot never **consumes** a slot - the returned index counts only the non-bot local players ahead
   of it. A human sitting behind two bots in `LocalPlayers[]` is still slot 0.

Because every local-player call site in the project already funnels through `GetLocalSlotIndex` or
`MyLocalPlayer.Slots`, that one exclusion is what keeps all of them bot-unaware. With one human in
the party, "the local player" and "the player the camera is following" are the same character by
construction.

`QuantumDebugInput.PollInput` polls empty input for a bot slot. Functionally this is belt-and-
braces (the simulation ignores it), but it matters for a real reason: the file's
`callback.PlayerSlot == 1 ? two : one` ternary means that with three local players, slots 0 **and**
2 both fall through to `PollPlayerOneInput` - so the human's own keys would otherwise be mirrored
onto a bot slot.

## Audio, camera and every other "is this mine" consumer

These needed **no bot-specific code at all** - they are downstream of the one `GetLocalSlotIndex`
exclusion above, and that is the whole point of fixing it there rather than at each call site.

- **Sound ownership** (`EntitySound.ResolveVolume`) asks `MyLocalPlayer.IsLocalEntity(owner)`. A bot
  is never in a slot, so it resolves as a remote player: `SoundData.quieterWhenRemote` scales it
  down by `AudioManager.remotePlayerVolume`, and `SoundData.localPlayerOnly` drops it entirely
  without even taking a voice. A bot Pixie's reload clicks, ability-ready cues and low-ammo
  warnings therefore stay out of the mix, exactly as a networked teammate's would - while her
  weapon, footsteps and explosions still play, spatialised and attenuated from wherever the human
  is standing. Every player-owned sound in the project already routes through `EntitySound`
  (`WeaponView`, `BlobAnimationView`, `SkillSoundView`, `AccessoryView`, `HeroLevelUpView`,
  `ContinuousHitscanView`, `FlyingCurrencyManager`), so there is no second path to fix.
- **The listening point** (`LocalPlayerAudioListener`) averages `MyLocalPlayer.Slots`, so it rides
  the human and never gets dragged toward a bot.
- **Voice barks** (`VoiceDirector.PollHeroCharge`) iterate the same slots - a bot never triggers a
  local-only bark.
- **Camera** (`FollowCamera` targets, added from `MyLocalPlayer.Register`) frames only the human.
- **Local-player world visuals** (`MovementRingView`'s move ring / target arrow) hide on a bot, via
  `QuantumHelper.IsLocalPlayer` directly.
- **HUD** - every `BindToSlot(0, ...)` widget, `GameplayUiController.choiceWindows[]`,
  `HurtOverlayUiWidget`, `DamageFeedbackManager` - all read slots or `IsLocalEntity`.

What bots DO still get is everything the project already gives a remote teammate: a floating
`CharacterUiWidget` (name/health), a `PartyHudWidget` entry, and their own world sounds and VFX.

## Authoring

Nothing needs to be generated or assigned. Bots work off the existing scene configuration:

1. Open `QuantumGameScene`, select the `QuantumRunnerLocalDebug` object.
2. Set `LocalPlayers` to size 3. Leave entry 0 as the human; tick **Is Bot** on entries 1 and 2.
3. Give each bot entry its own `PlayerAvatar` (a different hero prototype per bot is the point).
4. Optionally tune `RuntimeConfig` -> **Debug** -> **Bots**. Every `FP` there treats `0` as "use
   the built-in default", so an untouched config already behaves sensibly.

The menu/networked path (`MatchMakingConfig.RuntimePlayers`) works the same way, and deliberately
skips its usual `PlayerAvatar = localCharacterAvatar` overwrite for a bot entry so its authored
hero survives.

## Current status

Code-complete; compiles once Quantum codegen picks up the new `BotBrain.qtn`. `BotInputSystem` is
registered in `SystemSetup.User.cs` inside `GameplaySystemGroup`, immediately before `KCCSystem`.
No asset authoring is required at all - only the scene steps above. **Not yet verified in-Editor.**

## Known simplifications

- **No combat positioning.** Bots don't kite, take cover, spread from AoE, or aim their skill at
  anything - `AimSystem` picks the weapon target, and a skill fires wherever the bot is facing.
- **No pathfinding.** Wall deflection plus the leash teleport is the whole navigation budget.
- **`BotSettings` is a struct, so it has no field initializers** - which is why every `FP` treats 0
  as "unauthored" (`BotInputSystem.Or`) and both booleans are phrased as opt-*outs*
  (`DisableAutoLevelUpPick`, `DisableAutoBreathingSkipVote`).
- **Bots count as players everywhere else**, on purpose - `f.PlayerCount`-driven co-op scaling,
  `TalentUtility`'s shared talent OR, `RunFailureSystem`, XP thresholds. That is what makes a bot
  party a realistic test of co-op balance, but it does mean a 1-human/2-bot run is scaled as a
  3-player run.
- **A bot never uses a Chest, Cursed Rift or Blacksmith**, and never spends its Reroll charges. The
  one exception is the Store, and only for a **solo** bot (see "Solo behavior" above) - a bot with
  a leader still never opens it.
- **No cross-chunk pathfinding for a followed player** - wall deflection + ledge avoidance + the
  brute-force local grid detour (`TryMoveToward`/`TryFindGridPath`, see "Void avoidance" above) is
  the whole budget; there's no map-scale reachability check the way solo wander's
  `CollectReachableChunks` gives chunk/enemy/orb picking (a followed player is a live entity going
  wherever a human/other bot decides, not a pickable goal to reachability-filter in advance) - a
  genuinely stuck follow still falls through to the leash teleport, same as ever. Solo wander goes
  one step further: it also reachability-filters its own goal PICKS up front, and a goal that turns
  out unreachable even to the grid search is dropped and re-picked instead of leashed (there's no
  "home" position to return to while solo).
- **`MyLocalPlayer` is still capped at 2 local slots** - unchanged, and now only humans count
  toward it.
