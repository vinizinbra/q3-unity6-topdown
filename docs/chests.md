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
- **Opening a Chest still pauses the WHOLE party's `GameplaySystemGroup`**, even though only the
  player who walked into it gets a `LevelUpChoice` - everyone else just waits, exactly as they
  already wait out each other's real level-ups today. A genuine per-player-only pause isn't
  something Quantum's `SystemDisable` supports (it's whole-subtree-or-nothing per system group), so
  building one would be materially bigger/riskier scope than reusing the existing pause. This is a
  real co-op UX tradeoff, not an oversight - revisit if it feels bad in practice.

## How it reuses the level-up pipeline

`LevelUpUtility.BeginLevelUpScreen` (every connected player, sequence-driven category) and the new
`LevelUpUtility.BeginChestScreen(f, player, forcedCategory)` (one player, forced category) both call
a shared private `OpenUpgradeScreen(f, recipients, config, forcedCategory)` - roll for each
recipient, then pause if anyone actually got something. `RollOptionsFor` resolves
`forcedCategory ?? GetCategoryForLevel(config, level)`, so a Chest's category always wins over
whatever the current level's own sequence slot would have picked.

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

## Falling onto the ground (`MapGroundSettleSystem`)

A Chest is placed directly in the map asset (`MapEntityLink`, added implicitly by Quantum), not
spawned via `SpawnedEntitySpawner.Spawn` like a skill/projectile-spawned entity (Sentry, Vortex) -
so its authored `GroundOffset` used to sit inert, and it just hung at whatever raw
`Transform3D.Position.Y` was hand-placed in the editor. `MapGroundSettleSystem`
(`ISignalOnEntityPrototypeMaterialized`, registered in the always-on section of `SystemSetup.User.cs`
right after `PlayerInitSystem`) now runs the same `GroundOffsetUtility.Apply` raycast/settle logic
`SpawnedEntitySpawner` uses, gated on the entity having both `GroundOffset` and `MapEntityLink` so it
can't double-apply to an actual spawn. Result: a Chest with `GroundOffset.FallGravityMultiplier > 0`
now visibly falls onto the ground over several ticks at map load, same as Sentry does at cast time,
instead of snapping or staying frozen at its placed height.

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
   whole party's gameplay freezes, only that player's `UpgradeWindow` shows a screen locked to the
   Chest's own `Kind` (rendered via the right card family if `Kind == ChooseWeapon`, see
   `docs/level-up-upgrades.md`), the Chest doesn't re-trigger for a second nearby player mid-screen,
   and gameplay resumes cleanly for everyone the instant the picking player confirms (not waiting out
   the full timer if they pick early).
