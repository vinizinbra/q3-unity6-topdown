# Chests

A `Chest` is a world-placed pickup that opens the same level-up upgrade-choice screen
`docs/level-up-upgrades.md` describes, but forced to ONE Editor-authored `LevelUpCategory`
(HeroSkill/GlobalUpgrade/RiftMutation/WeaponPerk/ChooseWeapon) instead of following
`LevelUpConfig.LevelSequence`. It reuses that whole roll/pause/resolve pipeline rather than
introducing a second one - see "How it reuses the level-up pipeline" below. Read
`docs/level-up-upgrades.md` first if you haven't; this doc only covers what's specific to a Chest.

## Design choices (both explicitly confirmed with the user)

- **Auto-collects by walking into radius**, same idiom as every existing pickup (ExpOrb/ScrapOrb/
  RiftShardOrb/CoinOrb). This project has no interact button and no interactable/prompt pattern at
  all today (`Input.qtn` has no such button) - adding one would be its own separate feature.
- **Each Chest instance/prototype is typed to exactly one category** (`Chest.Kind`, set once in the
  Editor) - not randomized at runtime. A level can place a "Weapon Chest," a "Global Upgrade Chest,"
  etc. as distinct prefabs/prototypes.
- **Opening a Chest pauses the WHOLE party's `GameplaySystemGroup`**, exactly as a real level-up
  already does. A genuine per-player-only pause isn't something Quantum's `SystemDisable` supports
  (it's whole-subtree-or-nothing per system group), so this was always going to be a whole-party
  pause regardless of who gets a pick.
- **EVERY connected player gets their own `LevelUpChoice` rolled from the Chest's own forced
  category**, not just whoever physically walked into it - reversed from the original design (which
  gave only the finder a `LevelUpChoice`, everyone else just waited) after the user confirmed it felt
  wrong in practice: with only one real recipient, every other connected player had nothing to
  confirm and was silently treated as already-done, so the instant the one real recipient picked,
  `LevelUpSystem.AllConfirmed` saw the whole screen as resolved and closed it out from under everyone
  else before they'd gotten to choose anything. Rolling for every connected player (same recipient
  list `BeginLevelUpScreen` already builds, see `LevelUpUtility.GetConnectedPlayers`) makes a Chest
  behave exactly like a real level-up: the screen waits for every connected player to confirm (or the
  countdown to expire) before resolving for anyone.

## How it reuses the level-up pipeline

`LevelUpUtility.BeginLevelUpScreen` (every connected player, sequence-driven category) and
`LevelUpUtility.BeginChestScreen(f, player, forcedCategory)` (every connected player too, but a
forced category instead of the sequence-driven one) both build their recipient list via the same
shared `GetConnectedPlayers(f)` and then call a shared private
`OpenUpgradeScreen(f, recipients, config, forcedCategory)` - roll for each recipient, then pause if
anyone actually got something. `player` (the entity that physically collected the Chest) is now only
used for the `ChestOpened` view event, not for who gets a `LevelUpChoice`. `RollOptionsFor` resolves
`forcedCategory ?? GetCategoryForLevel(config, level)`, so a Chest's category always wins over
whatever the current level's own sequence slot would have picked, for every recipient.

The `f.Global->LevelUpScreenOpen == true` guard inside `OpenUpgradeScreen` is now load-bearing, not
just defensive. Before Chests existed, `ExpOrbSystem` (the sole caller of `ExperienceUtility.Grant`,
which is what triggers a new screen) was itself paused inside `GameplaySystemGroup` while a screen
was open - that alone made re-entrancy structurally impossible. `ChestSystem` is a **second,
independent** trigger of the same `Global.LevelUpScreenOpen` flag, so it can't rely on being paused
to prevent overlapping opens; the explicit flag check is what does that job now.

## `Chest.qtn`

```
component Chest
{
    LevelUpCategory Kind;
    FP PickupRadius;
    Boolean Opened;
}
```

`Opened` guards against being collected twice while the screen it opened is still resolving -
`ChestSystem` destroys the entity once this is true, rather than the same tick it's collected (unlike
an orb, which destroys itself immediately - a Chest needs to survive long enough to notice its own
screen has closed).

A new `ChestOpened` event (`Events.qtn`, `EntityRef Player`, `FPVector3 Position`, `LevelUpCategory
Kind`) is a View-only hook for VFX/SFX/animation - no simulation consumer.

## `ChestSystem.cs`

Same walk-into-radius broadphase idiom as `ExpOrbSystem`/`ScrapOrbSystem`
(`EnemyMovementUtility.FindPlayersInRadius` against `filter.Transform3D->Position`, then an exact
sqr-distance check against `Chest.PickupRadius`) - **except it does NOT scale that radius by
`CharacterStats.PickupRangeMultiplier`**. A Chest is a deliberate walk-up-to-open prop, not a passive
magnet-scaled collectible like a currency orb, so build choices that widen pickup range shouldn't
also widen how far away a Chest opens from.

**Registered in the always-on section of `SystemSetup.User.cs`, immediately after `LevelUpSystem`,
NOT inside `GameplaySystemGroup`.** This is a deliberate divergence from every orb system (which
stay *inside* the group specifically because they're each other's only re-entrancy guard against
`ExperienceUtility.Grant`). `ChestSystem` must keep ticking even while a screen (its own or someone
else's) has the group disabled, so it can:
- notice its own `Chest.Opened == true` and destroy itself once that screen resolves, and
- stay reachable for a second nearby Chest the instant the first screen closes, rather than being
  frozen alongside every paused gameplay system for the whole screen's duration.

**2026-08-29 bug fix - "reachable the instant the first screen closes" was too eager.** Because
`ChestSystem` runs immediately after `LevelUpSystem` every tick, a level-up screen resolving
(`LevelUpUtility.Resolve` sets `Global.LevelUpScreenOpen` false) and this Chest opening a brand new
one could both happen inside the SAME tick - `LevelUpScreenOpen` goes `true -> false -> true` without
ever being published as `false` in between, which `GameplayUiController`'s edge-detected
`LevelUpScreenOpen` polling can never observe. The simulation still correctly rolled the Chest's
options and re-disabled `GameplaySystemGroup` for it, but no window ever showed - a real, visible
freeze (player frozen, no card UI left to click) despite the simulation doing exactly what it's
supposed to. Same hazard `DebugCheatSystem.TryOpenNextPendingLevelUp` already found and fixed for its
own debug chain (`Global.DebugLevelUpScreenOpenLastTick`) - that field is captured too late in the
tick order to also protect `ChestSystem` (it's snapshotted by `DebugCheatSystem` itself, which runs
AFTER `ChestSystem`), so a new, earlier-captured `Global.LevelUpScreenOpenLastTick` was added
(snapshotted at the very top of `LevelUpSystem.Update`, before that tick's own `Resolve` can run) and
`ChestSystem`'s own guard now also checks it - a Chest can only claim the flag once it's been
observably closed for at least one full published tick.

Also fixed the same day: `ExperienceUtility.Grant`'s own `Global.Level` increment is unconditional,
but its call to `LevelUpUtility.BeginLevelUpScreen` was a single fire-and-forget attempt - if
`OpenUpgradeScreen`'s re-entrancy guard was blocked (a Chest's own screen already open) that pick was
silently lost forever, with the Level already spent and no retry. `Global.PendingLevelUpScreen` makes
this durable instead: set the instant `BeginLevelUpScreen` is called, cleared only once
`OpenUpgradeScreen` actually gets to run for that (non-Chest) request, retried every tick by
`LevelUpSystem.Update` in the meantime.

## Falling onto the ground (`GroundOffset` / `GroundSettleSystem`)

A Chest is placed directly in the map asset (`MapEntityLink`, added implicitly by Quantum), not
spawned via `SpawnedEntitySpawner.Spawn` like a skill/projectile-spawned entity (Sentry, Vortex) - so
nothing at its "spawn site" ever runs a ground check for it. It doesn't need one: `GroundOffset` is a
continuous, gravity-like component, not a one-shot placement pass. While `GroundOffset.Enabled` is
true, `GroundSettleSystem` re-resolves the real ground underneath the entity **every tick** and moves
`Transform3D.Position.Y` toward `groundY + <collider bottom clearance> + Offset`, then clears
`Enabled` the instant it arrives - so a landed Chest costs one bool check per tick and nothing else.
Author `Enabled` true on the prototype and it grounds itself the moment it exists, wherever it exists.

Result: a Chest with `GroundOffset.FallGravityMultiplier > 0` visibly falls onto the ground, same as
Sentry does at cast time, instead of snapping or staying frozen at its placed height.

### Why it's continuous, not resolved once at spawn (reworked 2026-08-21)

`GroundOffset` used to be a one-shot: `GroundOffsetUtility.Apply` raycast once, wrote a `TargetY` onto
a `SettlingToGround` marker, and `GroundSettleSystem` eased toward that baked value and removed the
marker on arrival. Map-baked entities got their one call from a `MapGroundSettleSystem` reacting to
`ISignalOnEntityPrototypeMaterialized` - and, as written, that could **never** work for any map-baked
entity (found via a floating `BreakableBarrel`; the Chest had the identical bug). That signal is
raised from `Core.EntityPrototypeSystem.OnInit`, i.e. before a single system's first `Update`, which
is two independent reasons too early for a ground raycast:

1. **The level doesn't exist yet.** Every chunk except the hand-placed Boss Arena is `f.Create`'d by
   `LevelGenerationSystem.GenerateLevel`, which runs in frame 0's `Update`. At materialize time there
   is literally no floor under a map-baked prop.
2. **Even the map's own entity colliders aren't queryable yet.** `f.Physics3D` queries only see what
   the last `Core.PhysicsSystem3D` update put in the broadphase, and at `OnInit` it has never run.

So the raycast always missed, logged `[GroundOffset] ... no ground was found beneath ... - left at
spawn Y`, and left the prop hanging in mid-air for the whole run.

Re-resolving every tick fixes that class of bug outright rather than patching the one trigger point:
an entity with nothing underneath it simply **holds position** (silently - a prop authored over a hole
would otherwise spam an error every tick, and falling into the void is strictly worse than hovering)
until there genuinely is ground, and starts falling then. It also stays correct for ground that
appears, moves or vanishes *later*, which a baked `TargetY` never could. `SettlingToGround` and
`MapGroundSettleSystem` were both deleted; `FallVelocity` moved onto `GroundOffset` itself.

`GroundOffsetUtility.Apply(f, entity)` survives as a pure **re-arm** (`Enabled = true`,
`FallVelocity = 0`, no raycast) for an entity *moved* mid-life -
`RelocationProtocolSkillAction` teleporting a Sentry to wherever Lux was standing is the canonical
case, since that Sentry cleared its own `Enabled` back when it first landed. The runtime spawn paths
(`SpawnedEntitySpawner`, `TalentGateSystem`, `OrbSpawnUtility`, `ExperienceUtility`) still call it as
a belt-and-braces guarantee that a spawn grounds itself even if a new prototype ships with the box
unticked.

Two supporting changes came with it:

- **`EnemyMovementUtility.TryFindGroundHeight` gained an optional `ignoreEntity`.** A caller asking
  "what is the ground under this thing" never means the thing itself. Without it, anything that both
  sits on the Ground layer and carries a `GroundOffset` (a level chunk) reads its own floor as the
  surface to rest on and climbs itself, one clearance per tick, forever.
- **`GroundSettleSystem` skips anything still carrying `PopVelocity`.** `PopMotionSystem` owns a
  popped orb's Y for the length of its ballistic arc and does its own per-tick ground resolve against
  the same `GroundOffset.Offset`; two systems integrating one Y would fight. On landing
  `PopMotionSystem` clears `GroundOffset.Enabled` itself, since it places the orb exactly at its
  resting height.

## Editor authoring needed (nothing shown at runtime without this)

1. **Chest `EntityPrototype`(s)** - at minimum one per `LevelUpCategory` worth placing, each with its
   own `Kind`/`PickupRadius` set, hand-placed directly in a level. Nothing spawns a Chest at runtime
   from a `RuntimeConfig.Prefabs` ref (unlike Exp/Scrap/RiftShard/Coin) - it's baked into the scene/
   level chunk like any other hand-placed prop. **Exception**: a talent-gated `SpawnEntityWithRequirement`
   component (see `docs/talents.md`) can reference any Chest `EntityPrototype` and have
   `TalentGateSystem` spawn it via `f.Create` at runtime instead - a Chest referenced this way
   isn't hand-placed in any chunk at all.
2. **`ChestOpened` view-side listener** (VFX/SFX/animation) - the event exists, nothing subscribes to
   it yet.
3. Whatever visual/collider the chest needs on the View side (open/closed model swap, etc.) - not
   covered by this doc, purely simulation + event plumbing.
4. **Manual end-to-end test not yet run**: walk one co-op player into a placed Chest and confirm the
   whole party's gameplay freezes, EVERY connected player's own `ChooseWindow` shows a screen locked
   to the Chest's own `Kind` (rendered via the right card family if `Kind == ChooseWeapon`, see
   `docs/level-up-upgrades.md`) with their own independently-rolled options, the Chest doesn't
   re-trigger for a second nearby player mid-screen, one player confirming does NOT close the screen
   for anyone still picking, and gameplay only resumes once every connected player has confirmed (or
   the countdown expires) - exactly like a real level-up.
