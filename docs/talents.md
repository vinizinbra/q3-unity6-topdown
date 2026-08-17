# Talents (meta-progression) + Lobby Start

Talents are small, permanent unlocks earned OUTSIDE a match - meta-progression, not an in-run pick
like Global Upgrades/Weapon Perks/Rift Mutations/Hero Ascensions (`docs/level-up-upgrades.md`).
They're persisted locally (see "Persistence" below) and carried into a match on `RuntimePlayer`,
the same mechanism that already existed for `RuntimePlayer.Talents.WeaponLevel` before this feature.

Lobby Start is a companion feature: the run doesn't actually begin (no enemy spawning, no
`Global.SurvivalTime` counting) until every connected player has physically walked outside the
level's starting chunk's own footprint - the same chunk (renamed `ChunkType.LobbyStart`, formerly
`ChunkType.Start`) that already anchors `Global.PlayerSpawnPosition`. That chunk's own `Chunk.
SpawnConfig` (see below) is where a starter chest gets configured so it spawns there once its
Talent is earned.

## Design choices (all explicitly confirmed with the user)

- **Talents are flat named fields, not a generic ID-keyed table.** `RuntimePlayer` carries one
  field per talent (`PlayerDamageLevel`, `HasWeaponChest`, etc.) rather than a `TalentId` enum +
  bitmask/asset-pool - same style `CharacterStats` already uses for its own flat multiplier/flag
  fields. Adding a new talent means adding a new field, not a new pool entry.
- **`Player*` fields are per-player** (byte, 0-5) - baked once into that player's own
  `CharacterStats` at spawn. **`Has*`/`Can*` fields are shared/coop** (bool) - OR'd across every
  connected player, so if *any* player has unlocked it, it's active for the whole group.
- **Every leveling talent uses the same flat +5%/level step** (`TalentsConfig.PercentPerLevel`,
  one shared tunable, not a per-stat curve). The two fraction-type stats (`CriticalChance`,
  `DamageReduction`) apply this as +5 flat percentage points/level instead of a relative multiply,
  since a relative multiply on a small base fraction barely moves the needle.
- **No boundary entity.** The `LobbyStart` chunk already carries everything needed to compute its
  own world-space footprint (`Transform3D.Position` + `Chunk.ChunkSizeWidth/Depth` +
  `Chunk.Rotation`), the same way `LevelGenerationSystem` already reads the Boss Arena's footprint
  back out for its own grid-origin math (`SpawnAtBossArenaDirectly`/`SeedFromExistingChunks`/
  `ComputeGridOrigin`) - so the chunk IS the lobby boundary, no separate hand-placed entity needed.
- **Talent-gated spawning is a `ChunkSpawnConfig` DataAsset (`Chunk.SpawnConfig`), holding an
  array of `SpawnEntityWithRequirement` entries - not a component, not Chest-specific.** Went
  through three earlier shapes before landing here: first a gate that pruned a hand-placed
  `Chest` if its talent wasn't met, then a `TalentsConfig`-driven auto-spawn with a computed
  radial offset, then a standalone `SpawnEntityWithRequirement` qtn *component* (configure
  Prototype/Offset/Requirement/Chance directly on an entity in the Editor). That component shape
  hit a real ECS wall almost immediately: an entity can only carry ONE instance of a given
  component type, so a single `LobbyStart` chunk wanting 3 independent conditional spawns (Weapon/
  Hero/GlobalUpgrade chests all at once) needed 3 separate entities, each carrying its own copy -
  awkward for something that's conceptually "one chunk's spawn table." Converting
  `SpawnEntityWithRequirement` into a plain C# struct living in a `SpawnEntityWithRequirement[]`
  array field on a new `ChunkSpawnConfig : AssetObject`, referenced via one `AssetRef<
  ChunkSpawnConfig> SpawnConfig` field added to `Chunk` itself, solves this directly - same
  "`AssetObject` array field, not a component" shape `LevelConfig.ChunkPool` (`ChunkPoolEntry[]`)
  already uses elsewhere in this codebase. Nested/child `EntityPrototype`s were also considered
  and explicitly ruled out along the way - Quantum's prefab->`EntityPrototype` importer only reads
  a prefab's ROOT GameObject (`QuantumEntityPrototypeAssetObjectImporter.cs:63-67`, a plain
  `TryGetComponent`, not `GetComponentsInChildren`), so nested `QuantumEntityPrototype`s in the
  same prefab are silently ignored - not usable for "child entities spawn together" at all in
  this SDK version.
- **`HasUnlockedRift`/`CanFindStones`/`HasEvent` are scaffolded only.** Declared, persisted,
  seeded, and aggregated exactly like the chest flags, but drive zero gameplay behavior yet - same
  shape as this codebase's other reserved-for-later fields (`BalanceConfig`'s
  `ExpectedPlayerDps`/`EliteFrequency`).
- **Lobby Start replaces `ChunkType.Start` in place** (rename, not a second chunk type) -
  `LevelGenerationSystem` already hard-assumes exactly one canonical "Start" chunk in several
  places (Boss-adjacency rejection, `AssignPlayerSpawnPosition`'s fallback), so a rename is far
  lower risk than teaching it to juggle two start-like types.

## `Talents.qtn`

```
enum SharedTalentRequirement : Byte
{
    None, WeaponChest, HeroChest, GlobalUpgradeChest, UnlockedRift, FindStones, Event
}

global
{
    Boolean TalentsResolved;
    Boolean SharedHasWeaponChest;
    Boolean SharedHasHeroChest;
    Boolean SharedHasGlobalUpgradeChest;
    Boolean SharedHasUnlockedRift;
    Boolean SharedCanFindStones;
    Boolean SharedHasEvent;
}
```

No component here anymore - `SpawnEntityWithRequirement` now lives as a plain C# struct in
`ChunkSpawnConfig.cs` (below), referenced from `Chunk` itself.

`Chest.qtn` is **not** touched by this feature at all - a talent-granted chest is just any Chest
`EntityPrototype`, referenced by `AssetRef` from a `SpawnEntityWithRequirement.Prototype` field.

## `ChunkSpawnConfig` (`Assets/_QuantumUser/Simulation/Assets/Config/ChunkSpawnConfig.cs`)

```csharp
[Serializable]
public struct SpawnEntityWithRequirement
{
    public AssetRef<EntityPrototype> Prototype;
    public FPVector3 Offset;
    public SharedTalentRequirement Requirement;
    public FP Chance;
}

public class ChunkSpawnConfig : AssetObject
{
    public SpawnEntityWithRequirement[] Spawns;
}
```

`Chunk.qtn` gained one new field, `AssetRef<ChunkSpawnConfig> SpawnConfig;` (defaults
unassigned = nothing spawns for that chunk). `TalentGateSystem` resolves every `Chunk` entity's
own `SpawnConfig` (if assigned) and every entry in its `Spawns` array exactly once, at level
start - `Offset` is added to that CHUNK entity's own `Transform3D.Position`, so the same struct
works for a fixed hand-placed chunk or a procedurally-positioned one like `LobbyStart` either way.
`Chance` keeps the same "un-authored `0` means no chance gate, always spawns" convention the old
component field had - a fresh array element defaults to its type's zero value either way (a plain
struct in a Unity Inspector array is never constructed with a custom initializer, same as a qtn
component field), so `0` reading as "always" rather than "never" is still the right no-op default
here, not just a component-specific quirk. Author an actual value in `(0, 1)` only when a spawn
should also be rare.

`Global.LobbyExited` (this doc's own field, briefly) has since been superseded by the structured
`GameState` enum (`Global.CurrentState == GameState.Survival`) - see **`docs/game-state.md`** for
the full match-flow state machine this is now part of. `LobbyBoundarySystem`/`CombatDirectorSystem`
below are both described in terms of that enum now, not a standalone boolean.

## `LevelGenerationSystem.TryGetLobbyStartBounds`

`SwapsAxes`/`MinCornerOffsetWorld` (private helpers `LevelGenerationSystem` already used
internally 3x for reading a hand-placed chunk's footprint back out) were promoted to `internal
static` - pure functions of their parameters, so this is a behavior-preserving visibility change,
not a refactor of the placement algorithm itself. A new `internal static
TryGetLobbyStartBounds(Frame f, out FPVector3 min, out FPVector3 max)` filters `Chunk +
Transform3D` for `Type == LobbyStart` and returns its world-space AABB (X/Z only - a chunk's
footprint has no meaningful Y extent, floor stays at Y=0 same as `CellToWorld`). Used only by
`LobbyBoundarySystem` (the lobby-exit check) - talent spawning doesn't need it, since
`SpawnEntityWithRequirement.Offset` is relative to whatever entity it's attached to, not to a
computed footprint center.

## `RuntimePlayer` fields

**Update (2026-08-07): grouped into one nested struct.** Every meta-progression field below now
lives on `RuntimePlayer.Talents` (`public PlayerTalents Talents;`, a plain nested `struct`, not a
class - so the field is never null, no separate initialization needed) instead of sitting directly
on `RuntimePlayer` itself. Every call site reads/writes through that one field (e.g.
`runtimePlayer.Talents.PlayerDamageLevel`, `RuntimePlayers[i].Talents.HasWeaponChest = ...`) - see
`RuntimePlayer.User.cs` for the actual struct definition.

```csharp
public struct PlayerTalents
{
    public byte WeaponLevel;                // not %-scaled - copied 1:1 -> CharacterStats.WeaponTalentLevel
    public byte RerollQuantity;             // not %-scaled - copied 1:1 -> CharacterStats.RerollQuantity
    public byte ShopWeaponOfferCount;       // not %-scaled - copied as +1 -> StoreUtility.ResolveWeaponOfferCount (see docs/store-blacksmith.md)
    public int StartingCoins;               // not %-scaled - copied 1:1 -> CharacterStats.Coins (a currency amount, not a 0-5 level, hence int not byte)
    public byte PlayerDamageLevel;          // +5%/level -> CharacterStats.DamageMultiplier
    public byte PlayerCooldownLevel;        // -5%/level -> DashCooldownMultiplier + SkillCooldownMultiplier
    public byte PlayerFireRateLevel;        // +5%/level -> AttackSpeedMultiplier
    public byte PlayerReloadSpeedLevel;     // +5%/level -> ReloadSpeedMultiplier
    public byte PlayerCriticalChanceLevel;  // +5pp/level -> CriticalChance (flat, not relative)
    public byte PlayerCriticalDamageLevel;  // +5%/level -> CriticalDamageMultiplier
    public byte PlayerMaxHealthLevel;       // +5%/level -> MaxHealthMultiplier (+ CharacterSystem.RefreshMaxHealth)
    public byte PlayerMaxShieldLevel;       // +5%/level -> MaxShieldMultiplier (+ CharacterSystem.RefreshMaxShield)
    public byte PlayerDamageReductionLevel; // +5pp/level -> DamageReduction (flat, not relative)
    public byte PlayerMoveSpeedLevel;       // +5%/level -> MoveSpeedMultiplier
    public byte PlayerPickupRangeLevel;     // +5%/level -> PickupRangeMultiplier
    public byte PlayerExperienceLevel;      // +5%/level -> ExperienceGainMultiplier
    public bool HasWeaponChest;             // shared - satisfies a SpawnEntityWithRequirement.Requirement == WeaponChest
    public bool HasHeroChest;               // shared - satisfies a SpawnEntityWithRequirement.Requirement == HeroChest
    public bool HasGlobalUpgradeChest;      // shared - satisfies a SpawnEntityWithRequirement.Requirement == GlobalUpgradeChest
    public bool HasUnlockedRift;            // shared - scaffolded only
    public bool CanFindStones;              // shared - scaffolded only
    public bool HasEvent;                   // shared - scaffolded only
}
```

Same "seeded once from outside, never written by the simulation" contract every field here follows
- see `PlayerTalents`' own comment in `RuntimePlayer.User.cs`. `WeaponLevel`/`RerollQuantity` are
NOT `Player*Level`-shaped (`TalentUtility.ApplyPerPlayerTalents` never touches either) - both are
raw flat counts copied 1:1 into their matching `CharacterStats` field (`WeaponTalentLevel`/
`RerollQuantity` respectively) rather than a %-per-level multiplier, since "percent bonus" of a
count doesn't mean anything the way it does for e.g. `MoveSpeedMultiplier`. `RerollQuantity`
specifically is deliberately a pre-run talent rather than an in-run-pickable Global Upgrade -
`CharacterStats.RerollQuantity` only ever decreases for the rest of a match, as
`LevelUpUtility.RerollOptionsFor` spends it to redraw the current level-up/Chest screen. Persisted
the same way as `WeaponLevel` - its own `PlayerPrefInt` (`MatchMakingConfig.RerollQuantityPref`,
key `"reroll_quantity"`), read in `StartRunner` right before `AddPlayer`. See
`docs/level-up-upgrades.md`'s "Reroll" section for the in-match spend side.

## `TalentsConfig`

```csharp
public FP PercentPerLevel = 5;
```

Just the leveling-talent tuning now - chest prototype/offset/chance moved onto
`SpawnEntityWithRequirement` itself, configured per-instance in the Editor rather than centrally.

## `TalentUtility.cs`

- `ComputeSharedTalents(f)` - loops `0..f.PlayerCount`, skipping any slot with no `RuntimePlayer`
  data (`f.PlayerCount` is the session's fixed max slot count, not how many players actually
  joined - `f.GetPlayerData(i)` returns `null` for an unjoined slot, same guard
  `LevelGenerationSystem.SpawnPendingPlayers` uses), OR-ing every connected player's `Has*`/`Can*`
  fields into the six `Global.Shared*` fields.
- `IsSatisfied(f, requirement)` - resolves a `SharedTalentRequirement` against the matching
  `Global.Shared*` field. Does NOT roll `Chance` - `TalentGateSystem` does that separately.
- `ApplyPerPlayerTalents(f, entity, runtimePlayer, stats)` - reads
  `TalentsConfig.PercentPerLevel`, then one call per stat through three tiny helpers
  (`ApplyBonus`/`ApplyReduction`/`ApplyFlat`), mirroring how compact
  `CharacterStatMultiplierUpgradeData` (`docs/global-upgrades.md`) already is for the same
  "one field, one number" shape. Called from `PlayerSpawnUtility.Spawn`, right after the existing
  `WeaponTalentLevel` bake. No-ops with an error log if `RuntimeConfig.TalentsConfig` isn't
  assigned, same tolerance every other optional config-asset consumer in this codebase has
  (e.g. `CombatDirectorSystem`).

## `TalentGateSystem.cs`

Unfiltered `SystemMainThread`, registered in the always-on section of `SystemSetup.User.cs` right
after `MapGroundSettleSystem` and before `ChestSystem` (so an entity spawned this tick is already
visible to that system's own filter this same tick). Each tick, while `!Global.TalentsResolved`:
waits for every connected player (same null-slot-skipping loop as `ComputeSharedTalents`) to have
spawned (`PlayerSpawnUtility.HasSpawned`) - the earliest tick every client is guaranteed identical,
fully-populated `RuntimePlayer` data for all players, avoiding a determinism hazard from resolving
the shared mask before a remote player's join has replicated. Once true: calls
`ComputeSharedTalents`, sets `TalentsResolved = true`, then filters every `Chunk + Transform3D`
entity in the level (`ResolveSpawners`) - for each with a valid `SpawnConfig` assigned, resolves
that `ChunkSpawnConfig` asset and iterates every entry in its `Spawns` array (`ResolveSpawn`).
For each entry: checks `TalentUtility.IsSatisfied` against its `Requirement`, rolls `Chance`
(`DamageUtility.RollChance`, skipped entirely if `Chance <= 0`), and if both pass, `f.Create`s
`Prototype` positioned at that CHUNK entity's own `Transform3D.Position + Offset`, then calls
`GroundOffsetUtility.Apply(f, spawned, spawnedTransform)` right after - same "`f.Create` -> set
`Position` -> `GroundOffsetUtility.Apply`" pattern every other runtime-spawn path in this codebase
already follows (`SpawnedEntitySpawner`, `CoinUtility`, `RiftShardUtility`, `ScrapUtility`,
`ExperienceUtility`). A spawned entity isn't `MapEntityLink`-tagged (it wasn't baked into the
scene), so `MapGroundSettleSystem` itself never reacts to it - that system is only the map-baked
counterpart to this same call, per its own doc comment, not a substitute for it. `Apply` no-ops
safely if the spawned prototype has no `GroundOffset` component at all.

**Known simplification**: this resolves once. A player who joins after the sweep has already run
doesn't retroactively spawn something their own talent alone would have earned for the group,
though their own per-player Talents still apply correctly at their own spawn (independent bake in
`PlayerSpawnUtility.Spawn`).

## `LobbyBoundarySystem.cs`

Unfiltered `SystemMainThread`, registered right after `ChestSystem`, before the pausable
`GameplaySystemGroup` opens (so it runs before `CombatDirectorSystem`, inside that group, later the
same tick). Each tick, while `Global.CurrentState == GameState.Lobby`: same "wait for every
connected player to have spawned" guard as `TalentGateSystem`, then calls
`LevelGenerationSystem.TryGetLobbyStartBounds` and checks every spawned `PlayerLink + Transform3D`
entity's position (X/Z) against that AABB - if any spawned player is still inside it, the lobby
hasn't been exited yet. No-ops (stays paused) if no `LobbyStart` chunk has been placed at all.
Transitions to `GameState.Survival` via `GameStateUtility.SetState` once every player is outside -
see `docs/game-state.md` for the full state machine this is now part of.

`CombatDirectorSystem.Update` (`Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorSystem.cs`)
gained one extra early-return line requiring `Global.CurrentState == GameState.Survival`, right
after its existing `PlayerSpawnUtility.IsReadyToSpawn` check - this is the single choke point that already gates
`Global.SurvivalTime` accumulation and all Director spawning, so nothing else needed touching for
"spawning/counting" to wait on the lobby.

## Persistence (`MatchMakingConfig.cs`)

Mirrors `WeaponTalentLevelPref` exactly, but as one `PlayerPrefObject<TalentSaveData>` JSON-blob
pref (`"player_talents"`) instead of eighteen separate `PlayerPrefInt`/`PlayerPrefBool` fields,
since Talents is now several heterogeneous fields rather than one scalar. Read once in
`StartRunner`, copied onto every entry in `RuntimePlayers` right before `AddPlayer` - same "same
value for all local players" limitation `WeaponLevel` already has for couch co-op (one local
Photon client, multiple local `RuntimePlayer` entries all get the same pref value); not a new
limitation introduced here.

Nothing in this codebase currently *writes* to `"player_talents"` - same gap `WeaponTalentLevelPref`
already has (per its own comment, "an account/profile screen elsewhere would be what actually
raises this over time"). Building that screen is out of scope for this pass.

## Editor authoring needed (nothing shown at runtime without this)

1. Open the Editor once so `.qtn` codegen regenerates before any of this compiles.
2. Author `TalentsConfig.asset` (`PercentPerLevel` defaults to 5) and assign to
   `RuntimeConfig.TalentsConfig` in both scenes.
3. **`Assets/_QuantumUser/Entities/LevelChunk/LevelChunk.prefab` needs manual cleanup** - it
   already has the OLD component-based `SpawnEntityWithRequirement` added from before this
   refactor (`QPrototypeSpawnEntityWithRequirement` in its serialized YAML). Once codegen
   regenerates without that component type, this will show as a missing/orphaned script in the
   Inspector - remove it, then instead assign an `AssetRef<ChunkSpawnConfig>` to the `Chunk`
   component's new `SpawnConfig` field.
4. Author a `ChunkSpawnConfig.asset` with one `Spawns` entry per conditional spawn (e.g. three
   entries for Weapon/Hero/GlobalUpgrade chests - `Prototype`/`Offset`/`Requirement`/`Chance` each,
   `Chance` left at 0 unless deliberately made rarer) and assign it to the `LobbyStart` chunk
   prototype's `Chunk.SpawnConfig` field (or any other chunk that should conditionally spawn
   something - not restricted to `LobbyStart`).
5. Sanity-check the renamed `LobbyStart` `ChunkPoolEntry` in `LevelConfig.asset` still resolves
   (ordinal-stable rename, but worth a look).
6. **Manual end-to-end test not yet run**: verify a `ChunkSpawnConfig` entry actually
   spawns/doesn't spawn based on the local `player_talents` pref, verify `Global.SurvivalTime`/
   enemy spawns stay at 0 until every player exits the `LobbyStart` footprint, and verify
   `PlayerXLevel` fields actually move their target `CharacterStats` fields at spawn
   (`PlayerMaxHealthLevel`/`PlayerMaxShieldLevel` specifically need their `RefreshMaxHealth`/
   `RefreshMaxShield` calls to actually show up on `Health`/`Shield`, not just on `CharacterStats`
   itself).
