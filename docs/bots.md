# Local-testing Bots (co-op autopilot)

A bot is a **real Quantum player slot whose `Input` is produced by the simulation instead of a
device**. It exists so a solo developer can test co-op: open several clients, flip the extra ones
to bots, and play alongside them. Two ways to get one:

- **Spawned bot** - `RuntimePlayer.IsBot` on a `LocalPlayers`/`RuntimePlayers` entry (e.g. 1 human +
  2 bots from `QuantumRunnerLocalDebug`).
- **Cheat** - CheatMenu ▸ Player ▸ **Become Bot** adds `BotBrain` to your own hero (your camera/HUD
  stay yours, the autopilot plays); **Recover From Bot** removes it and hands control back. Neither
  survives a death/respawn (Spawn re-reads the real `IsBot`).

It is a testing tool, not a shipping AI.

## The two halves

**Simulation** - a bot is just a player who happens to have a `BotBrain` component:

| File | Role |
| --- | --- |
| `Simulation/QTN/Bot/BotBrain.qtn` | `component BotBrain` (Input Data + goal/heading/formation/path state) and `enum BotGoal` |
| `Simulation/Systems/Bot/BotInputSystem.cs` | The brain: perceive, pick one goal, execute it - writes `BotBrain.Data` every tick |
| `Simulation/Systems/Bot/BotPerception.cs` | One enemy scan per tick (counts, nearest, Elites, threat direction) shared by everything |
| `Simulation/Systems/Bot/BotNavGrid.cs` | Level-wide walkability grid + A* + string-pulling |
| `Simulation/Systems/Bot/BotNavigation.cs` | Path following, steering, void avoidance, heading smoothing, leash teleport |
| `Simulation/Systems/Bot/BotSkillUtility.cs` | Per-hero Hero Skill rules + safe Dash |
| `Simulation/Systems/Bot/BotPoiUtility.cs` | Breathing POI use (Store/Blacksmith/Shrine/Cursed Rift) |
| `Simulation/Commands/CheatCommand.cs` + `CheatSystem` | `BecomeBot` / `RecoverFromBot` |
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

## The brain

Each tick `BotInputSystem` gathers a `BotPerception`, picks **one** `BotGoal` (`SelectGoal`), executes
it through `BotNavigation`, then lets `BotSkillUtility` decide Dash/Hero Skill. Goals, highest first:

| Goal | When | What it does |
| --- | --- | --- |
| Revive | A Downed teammate within 40 that nobody else is holding | Walks onto them, holds Hero Skill once `ContextInteraction` reports the Revive Available |
| Interact | Breathing, `BreathingAreaSecured`, no enemy within 6, a POI still wanted this Break | Walks into the POI's radius, then `BotPoiUtility.Use` |
| Retreat | Health < `RetreatHealthFraction` with an enemy within 6 | Moves along the threat direction, pulled toward the leader |
| Follow (regroup) | Leader further than `LeaderMaxDistance` | Back to the formation slot until within FollowDistance+Slack |
| Loot (close) | A pickup within 4 and no enemy within 3 | Walks onto it |
| Fight | Enemy within `EngageRange` **and** within `LeaderTetherRadius` of the leader (or within 4 of the bot - self-defence) | Holds 60 % of weapon range (1.5-8), strafes, backs off when crowded |
| Loot | Orb (XP/Coin/Rift Shard) or unopened Chest within `LootRange` (and the leader's tether) | Walks onto it |
| Recover | A landed `DroppedAccessory` within 30 - own, or a teammate's with `AccessoryGuardConfig.AllowAllyRecovery` (pickup returns it to its owner); with a leader, also within LeaderMaxDistance-2 of them so fetching it can't trip a regroup | Walks onto it; gives up for 8 s if standing on it 10 s without collecting |
| Follow | There is a leader | Formation slot |
| Explore | No leader at all | Elite anywhere reachable → nearest undiscovered chunk → nearest enemy anywhere |

- **Leader** = the lowest-PlayerRef **human** (no `BotBrain`) who isn't KO. With one, the bot plays
  the game but stays around them (tether + regroup); with none, it plays the whole map. Bots never
  follow other bots.
- **Sticky targets.** A Fight target is held 3 s, Explore 10 s, a POI/loot until used/collected
  (loot gives up after 6 s standing on it). Any goal change clears paths and stuck tracking
  (`SetGoal`).
- **Stuck** = wanted to move but stayed within 1 unit for 2.5 s → the target is ignored for 8 s
  (`IgnoredTarget`). While following, being stuck/blocked/too far for `LeashTimeout` teleports the
  bot to the leader - only onto solid ground, never while the leader is airborne.
- **Formation.** Each bot gets a side by its index among bots (145°, 215°, 100°, 260° off the
  leader's travel direction, 180 = behind), jittered ±15° every 15-25 s. The leader's heading is
  smoothed from **position deltas**, not `Aim.Angle` (auto-aim retargets constantly - that's what
  used to swing bots around in arcs). A slot without standable ground falls back to its mirror, then
  to the leader's spot.
- **Brute** closes to melee while Juggernaut is active (his damage lands on contact).

### Skills (BotSkillUtility)

Only pressed when the slot is `Ready` with a stack (never mid-activation - that would cancel and
recast), with a 1 s / 2 s re-press guard. Keyed off the Hero Skill's `SkillData` type:

| Hero | Rule |
| --- | --- |
| Brute (`JuggernautSkillData`) | 3+ enemies within AftershockRadius+1, an Elite there, or <50 % Health with an enemy within 3 |
| Max (`BerserkSkillData`) | 3+ enemies within 8, or an Elite within 8 |
| Pixie / Zara / Kai (`ProjectileSkillData`) | `Aim.Target` within the skill's Range and 3+ enemies within 4 of it, or it's an Elite |
| Lux (Sentry spawn action) | Any enemy within 10 and none of her sentries alive |
| anything else | 2+ enemies within 8 |

**Dash** is never fired blind (it moves 6 units with the KCC off and no ground check - the main way
bots used to land in water). Only to **escape** (enemy within 2.5 and low Health or 3+ around it),
away from the threat, trying ±30/60/90°; or to **catch up** to a leader >12 away along the heading
it's already safely walking with no enemy within 6. Both require `IsDashLandingSafe` (end point,
both sides of it, and the midpoint all standable).

### Breathing POIs (BotPoiUtility)

Opened, decided and closed **in the same tick** through each POI's own utility (no ChooseWindow
flashes on the bot's client), each kind once per Break (`*AttemptedAtBreathingIndex`):

- **Store** - only the weapon level-up, only if affordable. Never buys a new weapon (a random swap
  would throw away the hero's picks/perks mid-test).
- **Blacksmith** - cheapest affordable perk on offer, else walks away free.
- **Healing Shrine** - below 80 % Health.
- **Cursed Rift** - always confirms the rolled sacrifice.

Each can be turned off in `RuntimeConfig.Bots` (`DisableStore`, ...). Level-ups, the Breathing skip
vote and Team Challenge Ready are auto-handled as before (below). Chests are walk-in auto-collect, so
Loot covers them.

## Navigation (BotNavGrid + BotNavigation)

**Grid pathfinding.** `BotNavGrid` is a level-wide 1x1-unit walkability grid built once per level
(first time a bot asks, once `LevelGenerated` and 0.5 s of collider settling have passed): one
raycast straight down per cell over every Chunk footprint (plus 4 inner re-tries so a chunk seam
doesn't punch a hole) records the top-most Ground-layer height, or *void* (water and pits are the
same thing here). A step between neighbours is walkable when the rise is ≤ 1.05
(`MovementDataAsset.MaxLedgeHeight` = 1, auto-mantle) and the drop ≤ 4 - so walls (their tops read
as tall ground) and water both fall out of the same data. Cells touching void are **Edge** cells. A cell topped by a `GroundNotJumpable` collider (solid,
never mantled - the Ground-layer ray passes through it, so it's checked separately) is **Blocked**:
not walkable, but not a fall either, so it doesn't make its neighbours Edge cells.

- **Straight first.** If the grid says the line from bot to target stays on walkable, non-Edge cells
  with valid steps (`IsStraightWalkable`), the bot steers straight there - no A*.
- **Otherwise A\*** (8-connected, integer costs 10/14, no corner-cutting past void; Edge cells +25,
  climbs +4, drops past the auto-hop height +30 - so routes keep off shorelines), capped at 25k
  expansions, then **string-pulled** into a few straight legs stored in `BotBrain.Path` (32 max; a
  longer route re-plans from its end). Re-planned when the goal moves > 2.5, every 1.5 s, or when a
  leg is blocked by something the grid doesn't know (props, barrels).
- **Raycasts** use `BotNavigation.BotQuery` = Statics | Kinematics | **Dynamics** (some level geometry
is a dynamic entity) | **ComputeDetailedInfo** (`Hit3D.Point`/`Normal` are only real with it).
- **Not simulation state.** The grid is a pure function of static geometry, built in one go (never
  across ticks) and cached statically, keyed by `LevelGenSeed` + the chunk layout - every client,
  late joiners and resimulations included, sees the same grid at every tick it's used, so it can't
  desync. Geometry spawned later (Traversal platforms) isn't in it.

**Steering safety on top** (`BotNavigation.SteerToward`), the last line of defence:
Moves that come from the nav grid (straight-walkable or A* legs) **skip the wall slide entirely**:
the grid already knows every Ground-layer wall and which are climbable steps, and the slide's 2.5-unit
sphere-cast kept bots shuffling sideways short of a step they could walk up. Free directions (Retreat,
strafing) still slide, guarded by `IsClimbableStepAhead`, which mirrors the auto-mantle test (feet ray blocked within 2.5 - the slide's own reach - by a near-vertical
face - ramps don't count -, ray at 1 unit clear, not GroundNotJumpable). Seeing one **commits** the bot
to walking straight into it for 0.4 s (`ClimbCommitTimer` - no wall slide, no heading blend; without
it the slide and the climb alternated every tick and the bot flickered) - walking straight at it is
all the player processor's auto-mantle needs, the bot never presses Jump. Otherwise
`SteerAroundWalls` slides along walls; otherwise `SteerAroundWalls` slides along walls; `TryFindSafeDirection` rejects a direction whose ground runs
out (centre + two lateral probes 0.4 either side at 1.5 ahead, gaps > 1 unit = water), the ground ray starts above the climb
height and looks down, so a step up reads as floor rather than a hole; trying
±35/70/105°, last-used side first; `ApplyHeading` blends 50 % of last tick's heading so one-tick
corrections don't jitter. Probes are skipped while airborne; a bot knocked into a pit is handled by
`PlayerFallSystem` like any player. Explore/Interact target picks still pre-filter by
`Chunk.ConnectedChunks` reachability.

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

Nothing needs to be generated or assigned.

- **Spawned bots:** select `QuantumRunnerLocalDebug`, give `LocalPlayers` extra entries with **Is
  Bot** ticked and their own `PlayerAvatar`. The menu/networked path (`MatchMakingConfig.RuntimePlayers`)
  works the same way.
- **Multi-client testing:** open the clients, and on each one you want automated press CheatMenu ▸
  Player ▸ **Become Bot**; **Recover From Bot** takes it back.
- Tuning: `RuntimeConfig` → **Debug** → **Bots** (Follow, Leash, Leader, Combat, Formation, Breathing
  POIs, Flow). Every `FP` treats `0` as "use the built-in default".

## Current status

Rewritten 2026-10-07 (goal-based brain, per-hero skill rules, safe dash, POI use, heading smoothing,
lateral ledge probes, Recover From Bot cheat). Compiles; **not yet play-tested** - expect tuning of
the distances/thresholds in `BotInputSystem`'s defaults.

## Known simplifications

- **No dodging** of enemy telegraphs/projectiles - Retreat and the escape Dash react to proximity and
  Health only.
- **Level-up picks are random** (`LevelUpUtility.AutoConfirm`), rerolls never used.
- **Grid is static** - built from the level once; moving obstacles are only handled by steering and
  re-planning. A one-time build spike (one raycast per cell) happens the first time a bot moves.
- **`BotSettings` is a struct**, so every `FP` treats 0 as "unauthored" and booleans are opt-outs.
- **Bots count as players everywhere else** on purpose (co-op scaling, XP thresholds, talents) - a
  1-human/2-bot run is scaled as a 3-player run.
- **Never uses Traversal Challenges, Team Challenge rewards or Rerolls.**
- **`MyLocalPlayer` is still capped at 2 local slots** - only humans count toward it.
